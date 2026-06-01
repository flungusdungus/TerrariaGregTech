#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Steam;

// Port of SteamParallelMultiblockMachine. Shared controller for steam_grinder
// + steam_oven. Runs electric recipes paid in steam through the bound
// SteamHatchPartMachine. Parallelizes up to MaxParallels with upstream's
// 1.5x duration / (8/9) x parallels EUt multiplier / 32 EUt cap / LV-tier cap.
// Upstream addHandlerList(steamEnergy) collapses to overriding TryDrainEU
// directly through SteamEnergyRecipeHandler (same shape as SimpleSteamMachine).
public sealed class SteamParallelMultiblockMachine : WorkableMultiblockMachine
{
	// Upstream CONVERSION_RATE = 2.0 (both steam_grinder + steam_oven use LP).
	public const double CONVERSION_RATE = 2.0;

	protected override string Label => Definition?.Label ?? "Steam Parallel";

	// Upstream config default = 8.
	public int MaxParallels { get; private set; } = 8;
	public void SetMaxParallels(int value) => MaxParallels = value;

	private SteamEnergyRecipeHandler? _steamEnergy;

	// MP clients don't bind _parts (MultiblockControllerMachine.OnStructureFormed:222)
	// so _steamEnergy is null client-side. Snapshot rides SaveData for the panel.
	public long SteamStored   { get; private set; }
	public long SteamCapacity { get; private set; }

	public SteamParallelMultiblockMachine() : base() { }

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		foreach (var part in GetParts())
		{
			if (part is SteamHatchPartMachine sh && sh.Tank is { } tank)
			{
				_steamEnergy = new SteamEnergyRecipeHandler(tank, CONVERSION_RATE);
				RefreshSteamSnapshot();
				return;
			}
		}
		// Defensive - matcher's SetExactLimit(1) on STEAM already rejects this.
		SetUnformedReason("No steam input hatch bound", new[]
		{
			"Steam multis need exactly one Steam Input Hatch installed on a wall cell.",
		});
		OnStructureInvalid();
	}

	public override void OnStructureInvalid()
	{
		base.OnStructureInvalid();
		_steamEnergy = null;
		SteamStored = 0; SteamCapacity = 0;
	}

	public override void OnPartUnload()
	{
		base.OnPartUnload();
		_steamEnergy = null;
		SteamStored = 0; SteamCapacity = 0;
	}

	private void RefreshSteamSnapshot()
	{
		if (_steamEnergy == null) { SteamStored = 0; SteamCapacity = 0; return; }
		SteamStored   = _steamEnergy.Stored;
		SteamCapacity = _steamEnergy.Capacity;
	}

	protected override void OnTick()
	{
		base.OnTick();
		// Aligned to BroadcastNearby's 6-tick cadence.
		if (IsServer && IsFormed && (Terraria.Main.GameUpdateCount & 0x7) == 0)
			RefreshSteamSnapshot();
	}

	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["spm_steam"] = SteamStored;
		tag["spm_cap"]   = SteamCapacity;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		SteamStored   = tag.GetLong("spm_steam");
		SteamCapacity = tag.GetLong("spm_cap");
	}

	// EU surface routed through SteamEnergyRecipeHandler (read-only view).
	public override long EnergyStored
	{
		get => _steamEnergy?.StoredEu ?? 0;
		set { }
	}

	// Bypass base HandleEUThroughCapProxy; drain directly through the handler.
	public override ActionResult TryDrainEU(GTRecipe recipe, long voltage)
	{
		if (voltage <= 0) return ActionResult.SUCCESS;
		if (_steamEnergy == null)
			return ActionResult.Fail("gtceu.recipe.insufficient_eu", EURecipeCapability.CAP, IO.IN);
		return _steamEnergy.TryDrainEnergy(voltage, simulate: false)
			? ActionResult.SUCCESS
			: ActionResult.Fail("gtceu.recipe.insufficient_eu", EURecipeCapability.CAP, IO.IN);
	}

	public override RecipeModifier GetRecipeModifier() => SteamParallelModifier;

	// Verbatim upstream recipeModifier. 8/9 is upstream's 0.8888 truncation
	// (numerically identical within IEEE-754).
	private static readonly RecipeModifier SteamParallelModifier = new((machine, recipe) =>
	{
		if (machine is not SteamParallelMultiblockMachine steam)
			return ModifierFunction.NULL;
		if (RecipeHelper.GetRecipeEUtTier(recipe) > (int)VoltageTier.LV)
			return ModifierFunction.NULL;

		long eut = recipe.InputEUt.GetTotalEU();
		int parallels = ParallelLogic.GetParallelAmount(machine, recipe, steam.MaxParallels);
		double scale = 8.0 / 9.0;
		double eutMultiplier = (eut * scale * parallels <= 32)
			? (scale * parallels)
			: (32.0 / eut);

		return ModifierFunction.Builder()
			.InputModifier (ContentModifier.Multiplier_(parallels))
			.OutputModifier(ContentModifier.Multiplier_(parallels))
			.DurationMultiplier(1.5)
			.EutMultiplier(eutMultiplier)
			.Parallels(parallels)
			.Build();
	});
}
