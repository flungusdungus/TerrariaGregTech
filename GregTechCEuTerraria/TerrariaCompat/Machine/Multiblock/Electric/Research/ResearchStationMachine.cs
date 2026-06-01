#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric.Research;

// Port of ResearchStationMachine (+ nested ResearchStationRecipeLogic). Locks
// an Object Holder, draws EU + CWU/t over a duration_is_total_cwu recipe, then
// stamps the holder's blank orb with the recipe's research_id - the stamped
// orb unlocks assembly_line recipes via the Data Access Hatch.
// frontFacing holder-orientation check dropped (no 2D facing).
public class ResearchStationMachine : WorkableElectricMultiblockMachine, IOpticalComputationReceiver
{
	public IOpticalComputationProvider? ComputationProvider { get; private set; }
	public ObjectHolderMachine? ObjectHolder { get; private set; }

	public ResearchStationMachine() : base() { }

	protected override RecipeLogic CreateRecipeLogic() => new ResearchStationRecipeLogic();

	public override bool RegressWhenWaiting() => false;   // verbatim upstream

	public IOpticalComputationProvider? GetComputationProvider() => ComputationProvider;

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		ComputationProvider = null;
		ObjectHolder = null;
		foreach (var part in GetParts())
		{
			if (part is ObjectHolderMachine holder)
				ObjectHolder = holder;
			if (part is IOpticalComputationHatch hatch)
				ComputationProvider = hatch;
			else if (part is IOpticalComputationReceiver recv)
				ComputationProvider ??= recv.GetComputationProvider();
		}
		// Persist actionable reason - bailing after matcher ok=true clears state.Error.
		if (ObjectHolder == null)
		{
			SetUnformedReason("No Object Holder",
				new[] { "Place an Object Holder at the pedestal cell (H)." });
			OnStructureInvalid();
		}
		else if (ComputationProvider == null)
		{
			SetUnformedReason("No Computation Receiver Hatch",
				new[] { "Install a Computation Data Reception Hatch on a casing cell." });
			OnStructureInvalid();
		}
	}

	public override void OnStructureInvalid()
	{
		ComputationProvider = null;
		ObjectHolder?.SetLocked(false);
		ObjectHolder = null;
		base.OnStructureInvalid();
	}

	// DEVIATION: upstream's addDisplayText has the
	// `addComputationUsageExactLine(getMaxCWUt())` line commented out (TODO);
	// we surface capacity vs required so an under-powered station is visible.
	// Both are side-effect-free reads; server-resolved, synced via SaveData.
	private int _displayCapacityCwu;
	private int _displayReqCwu;
	public int DisplayCapacityCwu => _displayCapacityCwu;
	public int DisplayRequiredCwu => _displayReqCwu;

	protected override void OnTick()
	{
		base.OnTick();
		if (!IsServer || !IsFormed) return;
		// Read via the hatch's container, NOT GetMaxCWUt: the interface entry
		// adds the hatch to the seen set first; on bridged net topologies the
		// cycle guard short-circuits to 0 ("Available: 0" trap).
		_displayCapacityCwu = (ComputationProvider as OpticalComputationHatchMachine)?.GetAvailableCwu()
			?? ComputationProvider?.GetMaxCWUt() ?? 0;
		_displayReqCwu      = ResolveRequiredCwu();
	}

	private int ResolveRequiredCwu()
	{
		// Active recipe if running; else the station recipe whose items the holder
		// satisfies (simulate/non-mutating; no RequestCWUt switch-saturation effect).
		var cand = Recipe.GetLastRecipe();
		if (cand == null)
		{
			foreach (var r in TerrariaCompat.Recipes.RecipeRegistry.ForStation(GetRecipeType().RegistryName))
			{
				if (r.GetTickInputContents(CWURecipeCapability.CAP).Count == 0) continue;
				var items  = r.GetInputContents(ItemRecipeCapability.CAP);
				var fluids = r.GetInputContents(FluidRecipeCapability.CAP);
				if (TryMatchInputContents(r, items, fluids).IsSuccess) { cand = r; break; }
			}
		}
		int req = 0;
		if (cand != null)
			foreach (var c in cand.GetTickInputContents(CWURecipeCapability.CAP))
				if (c.Payload is int v) req += v;
		return req;
	}

	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["rsCapCwu"] = _displayCapacityCwu;
		tag["rsReqCwu"] = _displayReqCwu;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		_displayCapacityCwu = tag.GetInt("rsCapCwu");
		_displayReqCwu      = tag.GetInt("rsReqCwu");
	}

	// Port of ResearchStationRecipeLogic.
	public sealed class ResearchStationRecipeLogic : RecipeLogic
	{
		protected override IReadOnlyList<Type> ValidMachineClasses() =>
			new[] { typeof(ResearchStationMachine) };

		private ResearchStationMachine M => (ResearchStationMachine)Machine;

		// Send full _progress: research cycle advances by CWU DRAWN per tick (not 1),
		// so the client's flat _progress++ interpolation diverges. Same fix as
		// CleanroomLogic.
		public override void SaveForSync(Terraria.ModLoader.IO.TagCompound tag) => Save(tag);

		// Skip output-room check (output replaces the holder's slot - always fits).
		protected override ActionResult MatchRecipe(GTRecipe recipe)
		{
			var machine = GetRLMachine();
			var itemIn  = recipe.GetInputContents(ItemRecipeCapability.CAP);
			var fluidIn = recipe.GetInputContents(FluidRecipeCapability.CAP);
			var inResult = machine.TryMatchInputContents(recipe, itemIn, fluidIn);
			if (!inResult.IsSuccess) return inResult;
			return MatchTickRecipe(recipe);
		}

		// Port of ResearchStationRecipeLogic.checkMatchedRecipeAvailable (140-162).
		// Required so the station picks up CWU becoming available without an
		// item-change event: base impl leaves status IDLE on rejection, and
		// IDLE + no lastFailedMatches unsubscribes the tick (RecipeLogic.cs:272).
		// Upstream calls setWaiting(reason) -> WAITING -> IsIdle()=false -> stays
		// subscribed until CWU recovers.
		public override bool CheckMatchedRecipeAvailable(GTRecipe match)
		{
			var modified = GetRLMachine().FullModifyRecipe(match);
			if (modified != null)
			{
				// "What is the point of this" - verbatim upstream.
				if (modified.GetInputContents(CWURecipeCapability.CAP).Count == 0 &&
				    modified.GetTickInputContents(CWURecipeCapability.CAP).Count == 0)
				{
					return true;
				}

				// ADAPTATION (parity outcome, not literal): upstream reaches this
				// only for candidates its input-indexed lookup pre-matched against
				// the holder. We scan all recipes (SupportsRecipeLookup=false), so
				// without this gate setWaiting runs on every research recipe and the
				// station shows WAITING while idle. Gate setWaiting on items actually
				// matching the holder = the set upstream's lookup pre-filters to.
				var itemIn  = modified.GetInputContents(ItemRecipeCapability.CAP);
				var fluidIn = modified.GetInputContents(FluidRecipeCapability.CAP);
				if (!M.TryMatchInputContents(modified, itemIn, fluidIn).IsSuccess)
					return false;

				var recipeMatch = CheckRecipe(modified);
				if (recipeMatch.IsSuccess)
				{
					SetupRecipe(modified);
				}
				else
				{
					SetWaiting(recipeMatch.ReasonText());
				}
				if (_lastRecipe != null &&
				    GetStatus() == global::GregTechCEuTerraria.Api.Machine.Feature.RecipeLogicStatus.WORKING)
				{
					_lastOriginRecipe = match;
					lastFailedMatches = null;
					return true;
				}
			}
			return false;
		}

		// IN: lock holder (inputs not consumed - "replaced" on OUT).
		// OUT: clear item, stamp researched orb into data slot, unlock.
		protected override ActionResult HandleRecipeIO(GTRecipe recipe, IO io)
		{
			var holder = M.ObjectHolder;
			if (holder == null) return ActionResult.SUCCESS;

			if (io == IO.IN)
			{
				holder.SetLocked(true);
				return ActionResult.SUCCESS;
			}

			// OUT - "replace" the holder contents with the research output.
			if (_lastRecipe == null)
			{
				holder.SetLocked(false);
				return ActionResult.SUCCESS;
			}

			holder.SetHeldItem(new Item());
			var outItem = ResolveResearchOutput(_lastRecipe);
			if (outItem != null && !outItem.IsAir)
				holder.SetDataItem(outItem);
			holder.SetLocked(false);
			return ActionResult.SUCCESS;
		}

		// OUT has no tick outputs to emit; IN consumes normally.
		protected override ActionResult HandleTickRecipeIO(GTRecipe recipe, IO io)
		{
			if (io != IO.OUT) return base.HandleTickRecipeIO(recipe, io);
			return ActionResult.SUCCESS;
		}

		private static Item? ResolveResearchOutput(GTRecipe recipe)
		{
			foreach (var content in recipe.GetOutputContents(ItemRecipeCapability.CAP))
			{
				if (content.Payload is not Ingredient ing) continue;
				var (type, outNbt) = PeelItem(ing);
				if (type <= 0) continue;
				var stack = new Item();
				stack.SetDefaults(type);
				if (!string.IsNullOrEmpty(outNbt))
				{
					var (rid, rtype) = ParseResearch(outNbt!);
					if (!string.IsNullOrEmpty(rid))
					{
						var recType = GTRecipeType.Get(StripNs(rtype)) ?? recipe.RecipeType;
						ResearchManager.WriteResearchToStack(stack, rid, recType);
					}
				}
				return stack;
			}
			return null;
		}

		private static (int type, string? nbt) PeelItem(Ingredient ing) => ing switch
		{
			SizedIngredient s          => PeelItem(s.Inner),
			NBTPredicateIngredient nbt => (nbt.ItemType, nbt.OutputNbt),
			ItemStackIngredient isi    => (isi.ItemType, null),
			TagIngredient tag          => (tag.GetItems().Count > 0 ? tag.GetItems()[0].type : 0, null),
			_                          => (0, null),
		};

		// Extract research_id + research_type from the SNBT
		// `{assembly_line_research:{research_id:"...",research_type:"..."}}`.
		private static (string id, string type) ParseResearch(string snbt)
		{
			string id   = ExtractQuoted(snbt, "research_id");
			string type = ExtractQuoted(snbt, "research_type");
			return (id, type);
		}

		private static string ExtractQuoted(string snbt, string key)
		{
			int k = snbt.IndexOf(key, StringComparison.Ordinal);
			if (k < 0) return "";
			int q1 = snbt.IndexOf('"', k);
			if (q1 < 0) return "";
			int q2 = snbt.IndexOf('"', q1 + 1);
			if (q2 < 0) return "";
			return snbt.Substring(q1 + 1, q2 - q1 - 1);
		}

		private static string StripNs(string id)
		{
			int i = id.IndexOf(':');
			return i >= 0 ? id[(i + 1)..] : id;
		}
	}
}
