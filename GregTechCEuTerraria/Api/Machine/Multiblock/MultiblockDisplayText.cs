#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Chance.Boost;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Common.Energy;
using Terraria;
using Terraria.Localization;
using RecipeContent = GregTechCEuTerraria.Api.Recipe.Content.Content;

namespace GregTechCEuTerraria.Api.Machine.Multiblock;

// Port of com.gregtechceu.gtceu.api.machine.multiblock.MultiblockDisplayText.
//
// Builder that assembles a multiblock controller's status tooltip - energy
// usage, working state, progress, parallels, fuel needs, maintenance problems,
// per-recipe output rate lines, etc. Consumed by a future
// `MultiblockControllerMachine.OnAddFancyInformationTooltip` override.
//
// Adaptations: List<Component> -> List<string> (no Mojang rich-text);
// ChatFormatting colours dropped (TODO [c/RRGGBB:] once the tooltip renderer
// parses them); HoverEvent line-hover dropped (no Terraria analogue - add a
// visible line instead); Component.translatable -> Language.GetTextValue;
// FormattingUtil number formats -> ToString("N0") / ("0.00"); GTValues.VNF /
// getFloorTierByVoltage / getTierByVoltage -> VoltageTiers.ShortName /
// FloorTierByVoltage / TierByVoltage; enableMaintenance gate dropped on
// AddMaintenanceProblemLines; static factory `builder` -> `Create` (C# name
// collision with the nested Builder class).
public static class MultiblockDisplayText
{
	private const string EmptyComponent = "";

	// DEVIATION - upstream's verbatim line is
	// `Language.GetTextValue(reason)` against the raw `gtceu.recipe_logic.*`
	// / `gtceu.recipe_modifier.*` key, resolved through MC's lang file. We
	// route through this delegate which forwards to `RecipeStatusText.Resolve`
	// (TerrariaCompat-side) -> our existing `Mods.GregTechCEuTerraria.Recipe
	// Status.*` locale section. Same input key -> same English string, but
	// through a different locale namespace than upstream would. Kept because
	// it shares the translation table with the world-hover tooltip (single
	// source of truth) and `port-locale.py` already auto-mirrors upstream's
	// reason keys into RecipeStatus. The Api layer can't reference the
	// TerrariaCompat-side resolver directly, so the consumer installs the
	// mapping at `Mod.Load` (`MultiblockLocale.RegisterAll`). Default identity
	// - without the hook, raw key strings would leak to the GUI.
	public static Func<string, string> FailReasonResolver { get; set; } = id => id;

	// Construct a new Multiblock Display Text builder. Automatically adds the
	// "Invalid Structure" line if the structure is not formed.
	public static Builder Create(List<string> textList, bool isStructureFormed) =>
		Create(textList, isStructureFormed, true);

	public static Builder Create(List<string> textList, bool isStructureFormed,
		bool showIncompleteStructureWarning) =>
		new(textList, isStructureFormed, showIncompleteStructureWarning);

	public sealed class Builder
	{
		private readonly List<string> _textList;
		private readonly bool _isStructureFormed;

		private bool _isWorkingEnabled;
		private bool _isActive;

		// Three-state working-system translation keys (customisable by multis).
		private string _idlingKey  = "gtceu.multiblock.idling";
		private string _pausedKey  = "gtceu.multiblock.work_paused";
		private string _runningKey = "gtceu.multiblock.running";

		internal Builder(List<string> textList, bool isStructureFormed, bool showIncompleteStructureWarning)
		{
			_textList = textList;
			_isStructureFormed = isStructureFormed;

			if (!isStructureFormed && showIncompleteStructureWarning)
			{
				// Upstream: red "Invalid Structure" with a grey hover tooltip.
				// We surface both as visible lines (no hover affordance).
				textList.Add(Language.GetTextValue("gtceu.multiblock.invalid_structure"));
				textList.Add(Language.GetTextValue("gtceu.multiblock.invalid_structure.tooltip"));
			}
		}

		// Set the current working-enabled / active flags. Several add* methods
		// gate on these.
		public Builder SetWorkingStatus(bool isWorkingEnabled, bool isActive)
		{
			_isWorkingEnabled = isWorkingEnabled;
			_isActive = isActive;
			return this;
		}

		// Override the three-state "Idling" / "Paused" / "Running" keys; pass
		// null for any to keep the default.
		public Builder SetWorkingStatusKeys(string? idlingKey, string? pausedKey, string? runningKey)
		{
			if (idlingKey  is not null) _idlingKey  = idlingKey;
			if (pausedKey  is not null) _pausedKey  = pausedKey;
			if (runningKey is not null) _runningKey = runningKey;
			return this;
		}

		// === Energy lines ========================================================

		// Max EU/t the multi can use (per the bound energy container).
		public Builder AddEnergyUsageLine(IEnergyContainer? energyContainer)
		{
			if (!_isStructureFormed) return this;
			if (energyContainer != null && energyContainer.EnergyCapacity > 0)
			{
				long maxVoltage = Math.Max(energyContainer.InputVoltage, energyContainer.OutputVoltage);
				string energyFormatted = maxVoltage.ToString("N0");
				int voltageTier = VoltageTiers.FloorTierByVoltage(maxVoltage);
				string voltageName = VoltageTiers.ShortName((VoltageTier)voltageTier);
				_textList.Add(Language.GetTextValue("gtceu.multiblock.max_energy_per_tick",
					energyFormatted, voltageName));
			}
			return this;
		}

		// Max recipe tier this multi can use for recipe lookup.
		public Builder AddEnergyTierLine(int tier)
		{
			if (!_isStructureFormed) return this;
			if (tier < (int)VoltageTier.ULV || tier > (int)VoltageTier.MAX) return this;
			string voltageName = VoltageTiers.ShortName((VoltageTier)tier);
			_textList.Add(Language.GetTextValue("gtceu.multiblock.max_recipe_tier", voltageName));
			return this;
		}

		// Exact EU/t this multi needs to run.
		public Builder AddEnergyUsageExactLine(long energyUsage)
		{
			if (!_isStructureFormed) return this;
			if (energyUsage > 0)
			{
				string energyFormatted = energyUsage.ToString("N0");
				string voltageName = VoltageTiers.ShortName(
					(VoltageTier)VoltageTiers.TierByVoltage(energyUsage));
				_textList.Add(Language.GetTextValue("gtceu.multiblock.energy_consumption",
					energyFormatted, voltageName));
			}
			return this;
		}

		// Max EU/t this multi can PRODUCE. Skips itself if the recipe needs
		// more than the multi can output (recipeEUt is the recipe's EUt; both
		// are signed so the comparison is upstream-verbatim).
		public Builder AddEnergyProductionLine(long maxVoltage, long recipeEUt)
		{
			if (!_isStructureFormed) return this;
			if (maxVoltage != 0 && maxVoltage >= -recipeEUt)
			{
				string energyFormatted = maxVoltage.ToString("N0");
				string voltageName = VoltageTiers.ShortName(
					(VoltageTier)VoltageTiers.FloorTierByVoltage(maxVoltage));
				_textList.Add(Language.GetTextValue("gtceu.multiblock.max_energy_per_tick",
					energyFormatted, voltageName));
			}
			return this;
		}

		// Max EU/t including amps. Recommended for multi-amp output multis.
		public Builder AddEnergyProductionAmpsLine(long maxVoltage, int amperage)
		{
			if (!_isStructureFormed) return this;
			if (maxVoltage != 0 && amperage != 0)
			{
				string energyFormatted = maxVoltage.ToString("N0");
				string voltageName = VoltageTiers.ShortName(
					(VoltageTier)VoltageTiers.FloorTierByVoltage(maxVoltage));
				_textList.Add(Language.GetTextValue("gtceu.multiblock.max_energy_per_tick_amps",
					energyFormatted, amperage, voltageName));
			}
			return this;
		}

		// === Computation lines (HPCA / research-station family) =================

		public Builder AddComputationUsageLine(int maxCWUt)
		{
			if (!_isStructureFormed) return this;
			if (maxCWUt > 0)
			{
				_textList.Add(Language.GetTextValue("gtceu.multiblock.computation.max",
					maxCWUt.ToString("N0")));
			}
			return this;
		}

		public Builder AddComputationUsageExactLine(int currentCWUt)
		{
			if (!_isStructureFormed) return this;
			if (_isActive && currentCWUt > 0)
			{
				_textList.Add(Language.GetTextValue("gtceu.multiblock.computation.usage",
					currentCWUt.ToString("N0") + " CWU/t"));
			}
			return this;
		}

		// === Working-status three-state =========================================

		// Adds the running/paused/idling line based on current flags.
		public Builder AddWorkingStatusLine()
		{
			if (!_isStructureFormed) return this;
			if (!_isWorkingEnabled) return AddWorkPausedLine(false);
			if (_isActive) return AddRunningPerfectlyLine(false);
			return AddIdlingLine(false);
		}

		// "Work Paused." Added if working is not enabled, or if checkState=false.
		public Builder AddWorkPausedLine(bool checkState)
		{
			if (!_isStructureFormed) return this;
			if (!checkState || !_isWorkingEnabled)
				_textList.Add(Language.GetTextValue(_pausedKey));
			return this;
		}

		// "Running Perfectly." Added if active, or if checkState=false.
		public Builder AddRunningPerfectlyLine(bool checkState)
		{
			if (!_isStructureFormed) return this;
			if (!checkState || _isActive)
				_textList.Add(Language.GetTextValue(_runningKey));
			return this;
		}

		// "Idling." Added if not active and working enabled, or if checkState=false.
		public Builder AddIdlingLine(bool checkState)
		{
			if (!_isStructureFormed) return this;
			if (!checkState || (_isWorkingEnabled && !_isActive))
				_textList.Add(Language.GetTextValue(_idlingKey));
			return this;
		}

		// === Progress lines =====================================================

		public Builder AddProgressLineOnlyPercent(double progressPercent)
		{
			if (!_isStructureFormed || !_isActive) return this;
			int currentProgress = (int)(progressPercent * 100);
			_textList.Add(Language.GetTextValue("gtceu.multiblock.progress_percent", currentProgress));
			return this;
		}

		public Builder AddProgressLine(RecipeLogic recipeLogic)
		{
			if (recipeLogic.HasCustomProgressLine())
				return AddCustomProgressLine(recipeLogic);
			return AddProgressLine(recipeLogic.GetProgress(), recipeLogic.GetMaxProgress(),
				recipeLogic.GetProgressPercent());
		}

		// Current/max recipe duration (in ticks) + percent.
		public Builder AddProgressLine(double currentDuration, double maxDuration, double progressPercent)
		{
			if (!_isStructureFormed || !_isActive) return this;
			int currentProgress = (int)(progressPercent * 100);
			double currentInSec = currentDuration / 20.0;
			double maxInSec     = maxDuration     / 20.0;
			_textList.Add(Language.GetTextValue("gtceu.multiblock.progress",
				currentInSec.ToString("0.00"),
				maxInSec.ToString("0.00"),
				currentProgress));
			return this;
		}

		// Custom per-multiblock progress line via RecipeLogic.GetCustomProgressLine.
		public Builder AddCustomProgressLine(RecipeLogic recipeLogic)
		{
			if (!_isStructureFormed || !_isActive) return this;
			string? line = recipeLogic.GetCustomProgressLine();
			if (line is not null) _textList.Add(line);
			return this;
		}

		// Per-recipe setup-failure reasons (e.g. missing input, no cleanroom).
		public Builder AddRecipeFailReasonLine(RecipeLogic recipeLogic)
		{
			if (!_isStructureFormed || !recipeLogic.IsIdle()) return this;
			var reasons = recipeLogic.GetFailureReasons();
			if (reasons.Count == 0) return this;

			// DEVIATION - upstream does NOT dedupe.
			// Upstream's failure list collects per-candidate per-tick, so the
			// same reason (e.g. `insufficient_in`) recurs once per failed
			// recipe candidate (30+ identical lines). LowDragLib's
			// `ComponentPanelWidget` is scrollable so upstream gets away with
			// it; our `UIMultiLineDynamicLabel` is a flat stack and would fill
			// the panel with duplicates. Dedupe via HashSet keeps the panel
			// readable. Hard rule applied: deviation surfaced, asked, approved.
			HashSet<string>? seen = null;
			bool headerAdded = false;
			foreach (var reason in reasons)
			{
				string text = FailReasonResolver(reason);
				seen ??= new HashSet<string>();
				if (!seen.Add(text)) continue;
				if (!headerAdded)
				{
					_textList.Add(Language.GetTextValue("gtceu.recipe_logic.setup_fail"));
					headerAdded = true;
				}
				_textList.Add(" - " + text);
			}
			return this;
		}

		// === Batch / parallel info ==============================================

		public Builder AddBatchModeLine(bool batchEnabled, int batchAmount)
		{
			if (batchEnabled && batchAmount > 0)
			{
				_textList.Add(Language.GetTextValue("gtceu.multiblock.batch_enabled",
					batchAmount.ToString("N0")));
			}
			return this;
		}

		public Builder AddSubtickParallelsLine(int subtickParallels)
		{
			if (subtickParallels > 1)
			{
				_textList.Add(Language.GetTextValue("gtceu.multiblock.subtick_parallels",
					subtickParallels.ToString("N0")));
			}
			return this;
		}

		public Builder AddTotalRunsLine(int totalRuns)
		{
			if (totalRuns > 1)
			{
				_textList.Add(Language.GetTextValue("gtceu.multiblock.total_runs",
					totalRuns.ToString("N0")));
			}
			return this;
		}

		// === Output lines per recipe ============================================

		// Iterates the recipe's item + fluid outputs and emits a rate line per
		// output ("X per Y sec" or "Y sec per X"). Honours chanced outputs via
		// ChanceBoostFunction.
		public Builder AddOutputLines(GTRecipe? recipe)
		{
			if (!_isStructureFormed || !_isActive) return this;
			if (recipe is null) return this;

			int recipeTier = RecipeHelper.GetPreOCRecipeEuTier(recipe);
			int chanceTier = recipeTier + recipe.OcLevel;
			var function   = recipe.RecipeType.ChanceFunction;
			double maxDurationSec = recipe.Duration / 20.0;
			var itemOutputs  = recipe.GetOutputContents(ItemRecipeCapability.CAP);
			var fluidOutputs = recipe.GetOutputContents(FluidRecipeCapability.CAP);
			int runs = recipe.GetTotalRuns();

			foreach (var item in itemOutputs)
			{
				bool rounded = false;
				Terraria.Item stack;
				int count = 0;
				double countD = 1;
				string displaycount;
				if (item.Payload is IntProviderIngredient provider)
				{
					rounded = true;
					var maxStack = provider.GetMaxSizeStack();
					if (maxStack.Count == 0) continue;
					stack = maxStack[0];
					displaycount = Language.GetTextValue("gtceu.gui.content.range",
						provider.CountProvider.GetMinValue(),
						provider.CountProvider.GetMaxValue());
					if (item.Chance < item.MaxChance)
					{
						countD = countD * runs * function.GetBoostedChance(item, recipeTier, chanceTier) / item.MaxChance;
					}
					countD = countD * provider.GetMidRoll();
				}
				else
				{
					var stacks = ItemRecipeCapability.CAP.Of(item.Payload).GetItems();
					if (stacks.Count == 0) continue;
					stack = stacks[0];
					count = stack.stack;
					countD *= count;
					if (item.Chance < item.MaxChance)
					{
						rounded = true;
						countD = countD * runs * function.GetBoostedChance(item, recipeTier, chanceTier) / item.MaxChance;
					}
					count = Math.Max(1, (int)Math.Round(countD));
					displaycount = count.ToString();
				}
				string itemName = Lang.GetItemName(stack.type).Value;
				if (countD < maxDurationSec)
				{
					string key = "gtceu.multiblock.output_line." + (rounded ? "2" : "0");
					_textList.Add(Language.GetTextValue(key, itemName, displaycount,
						(maxDurationSec / countD).ToString("0.00")));
				}
				else
				{
					string key = "gtceu.multiblock.output_line." + (rounded ? "3" : "1");
					_textList.Add(Language.GetTextValue(key, itemName, displaycount,
						(countD / maxDurationSec).ToString("0.00")));
				}
			}

			foreach (var fluid in fluidOutputs)
			{
				bool rounded = false;
				Api.Fluids.FluidStack stack;
				int amount;
				double amountD = 1;
				string displaycount;
				if (fluid.Payload is IntProviderFluidIngredient provider)
				{
					rounded = true;
					var maxStack = provider.GetMaxSizeFluid();
					if (maxStack.Length == 0) continue;
					stack = maxStack[0];
					displaycount = Language.GetTextValue("gtceu.gui.content.range",
						provider.CountProvider.GetMinValue(),
						provider.CountProvider.GetMaxValue());
					if (fluid.Chance < fluid.MaxChance)
					{
						amountD = amountD * runs * function.GetBoostedChance(fluid, recipeTier, chanceTier) / fluid.MaxChance;
					}
					amountD = amountD * provider.GetMidRoll();
				}
				else
				{
					var stacks = FluidRecipeCapability.CAP.Of(fluid.Payload).GetStacks();
					if (stacks.Length == 0) continue;
					stack = stacks[0];
					amount = stack.Amount;
					amountD *= amount;
					if (fluid.Chance < fluid.MaxChance)
					{
						rounded = true;
						amountD = amountD * runs * function.GetBoostedChance(fluid, recipeTier, chanceTier) / fluid.MaxChance;
					}
					amount = Math.Max(1, (int)Math.Round(amountD));
					displaycount = amount.ToString();
				}
				string fluidName = stack.Type?.DisplayName ?? "?";
				if (amountD < maxDurationSec)
				{
					string key = "gtceu.multiblock.output_line." + (rounded ? "2" : "0");
					_textList.Add(Language.GetTextValue(key, fluidName, displaycount,
						(maxDurationSec / amountD).ToString("0.00")));
				}
				else
				{
					string key = "gtceu.multiblock.output_line." + (rounded ? "3" : "1");
					_textList.Add(Language.GetTextValue(key, fluidName, displaycount,
						(amountD / maxDurationSec).ToString("0.00")));
				}
			}
			return this;
		}

		// === Mode / parallels / warnings ========================================

		// "Mode: <recipe type>" - for multis that flip between recipe types.
		public Builder AddMachineModeLine(GTRecipeType recipeType, bool hasMultipleModes)
		{
			if (!_isStructureFormed || !hasMultipleModes) return this;
			// Upstream: registryName.toLanguageKey() -> "<namespace>.<path>" - for
			// `gtceu:electric_blast_furnace` that's `gtceu.electric_blast_furnace`.
			// Our RegistryName is the bare path, so prepend the gtceu namespace.
			string modeName = Language.GetTextValue($"gtceu.{recipeType.RegistryName}");
			_textList.Add(Language.GetTextValue("gtceu.gui.machinemode", modeName));
			return this;
		}

		public Builder AddParallelsLine(int numParallels) => AddParallelsLine(numParallels, false);

		public Builder AddParallelsLine(int numParallels, bool exact)
		{
			if (!_isStructureFormed) return this;
			if (numParallels > 1)
			{
				string key = exact ? "gtceu.multiblock.parallel.exact" : "gtceu.multiblock.parallel";
				_textList.Add(Language.GetTextValue(key, numParallels.ToString("N0")));
			}
			return this;
		}

		public Builder AddLowPowerLine(bool isLowPower)
		{
			if (!_isStructureFormed) return this;
			if (isLowPower)
				_textList.Add(Language.GetTextValue("gtceu.multiblock.not_enough_energy"));
			return this;
		}

		public Builder AddLowComputationLine(bool isLowComputation)
		{
			if (!_isStructureFormed) return this;
			if (isLowComputation)
				_textList.Add(Language.GetTextValue("gtceu.multiblock.computation.not_enough_computation"));
			return this;
		}

		public Builder AddLowDynamoTierLine(bool isTooLow)
		{
			if (!_isStructureFormed) return this;
			if (isTooLow)
				_textList.Add(Language.GetTextValue("gtceu.multiblock.not_enough_energy_output"));
			return this;
		}

		// === Maintenance ========================================================

		// Bitfield: bits 0..5 = wrench / screwdriver / soft_mallet / hammer /
		// wire_cutter / crowbar problems. A `0` bit = problem present. Upstream
		// gates on a ConfigHolder flag we don't have - gate dropped.
		public Builder AddMaintenanceProblemLines(byte maintenanceProblems)
		{
			if (!_isStructureFormed) return this;
			if (maintenanceProblems <= 0b111111 && maintenanceProblems > 0)
			{
				AddMaintenanceProblemHeader();
				if ((maintenanceProblems        & 1) == 0)
					_textList.Add(Language.GetTextValue("gtceu.multiblock.universal.problem.wrench"));
				if (((maintenanceProblems >> 1) & 1) == 0)
					_textList.Add(Language.GetTextValue("gtceu.multiblock.universal.problem.screwdriver"));
				if (((maintenanceProblems >> 2) & 1) == 0)
					_textList.Add(Language.GetTextValue("gtceu.multiblock.universal.problem.soft_mallet"));
				if (((maintenanceProblems >> 3) & 1) == 0)
					_textList.Add(Language.GetTextValue("gtceu.multiblock.universal.problem.hard_hammer"));
				if (((maintenanceProblems >> 4) & 1) == 0)
					_textList.Add(Language.GetTextValue("gtceu.multiblock.universal.problem.wire_cutter"));
				if (((maintenanceProblems >> 5) & 1) == 0)
					_textList.Add(Language.GetTextValue("gtceu.multiblock.universal.problem.crowbar"));
			}
			return this;
		}

		private void AddMaintenanceProblemHeader() =>
			_textList.Add(Language.GetTextValue("gtceu.multiblock.universal.has_problems"));

		// Two-line "Muffler obstructed!" + tooltip-line warning.
		public Builder AddMufflerObstructedLine(bool isObstructed)
		{
			if (!_isStructureFormed) return this;
			if (isObstructed)
			{
				_textList.Add(Language.GetTextValue("gtceu.multiblock.universal.muffler_obstructed"));
				_textList.Add(Language.GetTextValue("gtceu.multiblock.universal.muffler_obstructed.tooltip"));
			}
			return this;
		}

		// "Fuel needed: <name> (ticks: <N>)" - for the steam-turbine family.
		public Builder AddFuelNeededLine(string? fuelName, int previousRecipeDuration)
		{
			if (!_isStructureFormed || !_isActive || fuelName is null) return this;
			_textList.Add(Language.GetTextValue("gtceu.multiblock.turbine.fuel_needed",
				fuelName, previousRecipeDuration.ToString("N0")));
			return this;
		}

		// === Misc ===============================================================

		public Builder AddEmptyLine()
		{
			_textList.Add(EmptyComponent);
			return this;
		}

		// Hand the list to caller-supplied logic for custom additions.
		public Builder AddCustom(Action<List<string>> customConsumer)
		{
			customConsumer(_textList);
			return this;
		}

		// Current EU/t this turbine is producing.
		public Builder AddCurrentEnergyProductionLine(long euOutput)
		{
			_textList.Add(Language.GetTextValue("gtceu.multiblock.turbine.energy_per_tick_maxed",
				euOutput.ToString("N0")));
			return this;
		}
	}
}
