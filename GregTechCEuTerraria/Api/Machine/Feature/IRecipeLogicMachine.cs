#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Recipe;
using Microsoft.Xna.Framework;

namespace GregTechCEuTerraria.Api.Machine.Feature;

// PARTIAL - port of
// com.gregtechceu.gtceu.api.machine.feature.IRecipeLogicMachine.
//
// Feature interface for machines that host a RecipeLogic trait. Surface
// covers the upstream-shaped callbacks RecipeLogic calls into, plus the
// I/O hooks we provide in lieu of upstream's IRecipeCapabilityHolder
// capability-proxy map (collapsed to per-cap items/fluids args since we
// only have ItemRecipeCapability + FluidRecipeCapability at the recipe
// level today).
//
// I/O methods return ActionResult instead of bool so RecipeLogic can
// distinguish EU-input failure from other failures - the
// `runAttempt`-ramping force-SUSPEND for multiblock controllers gates on
// `result.io == IO.IN && result.capability == EURecipeCapability.CAP`,
// verbatim with upstream RecipeLogic.handleRecipeWorking.
public interface IRecipeLogicMachine : IWorkable
{
	// Whether the machine is currently in a state where recipes should be
	// considered (not stuck on no-power, no-redstone, etc.).
	bool IsRecipeLogicAvailable() => true;

	// True = stay subscribed to tick callbacks even when idle (poll every 5
	// ticks). False = unsubscribe between ticks; we get re-subscribed by
	// state-change events. Matches upstream semantic precisely.
	bool KeepSubscribing() => false;

	// Hook called before / during / after the per-tick work cycle. Default
	// no-ops; subclasses override for visual / state side-effects.
	bool BeforeWorking(GTRecipe recipe) => true;
	bool OnWorking() => true;
	void OnWaiting() { }
	void AfterWorking() { }

	// If true, after each recipe completes re-applies fullModifyRecipe to the
	// next round's origin recipe so a mid-batch tier change (e.g. swapping a
	// HV hatch for EV) immediately picks up the new overclock. Upstream
	// default is TRUE - "make it *always* do overclock and parallel so that
	// the machine doesn't get stuck running a lower-tier recipe in any
	// possible scenario" (verbatim upstream comment).
	bool AlwaysTryModifyRecipe() => true;

	// True = this is a multiblock controller (used by RecipeLogic's force-
	// SUSPEND retry path).
	bool IsMultiblockController() => false;

	// Prevents the machine from suspending on power-fail.
	bool PreventPowerFail() => false;

	// Custom progress-line text in the GUI. Default false = standard "X / Y
	// ticks" rendering.
	bool HasCustomProgressLine() => false;

	// True = progress regresses while WAITING (brownout). Upstream default
	// TRUE - required so a power-loss event makes the recipe genuinely re-
	// cycle rather than pause forever at high progress. Overridden FALSE by
	// generator multis (combustion / turbine) where progress is paused
	// instead. Mirrors upstream's MachineDefinition.regressWhenWaiting default.
	bool RegressWhenWaiting() => true;

	// Notification hook for status transitions (IDLE->WORKING etc.).
	void NotifyStatusChanged(RecipeLogicStatus oldStatus, RecipeLogicStatus newStatus) { }

	// Apply machine-side recipe modifications (overclock, parallel, EBF
	// temperature bonus, etc.). Default = identity.
	GTRecipe? FullModifyRecipe(GTRecipe recipe) => recipe;

	// If `FullModifyRecipe` returned null on its last call, the reason key the
	// rejecting modifier set via `ModifierFunction.Cancel(reason)`. Captured by
	// implementations that route through `RecipeModifier.GetModifier(...).Apply
	// (...)`. RecipeLogic.CheckMatchedRecipeAvailable reads this to surface a
	// useful failure on the world-hover tooltip ("No rotor installed",
	// "Out of lubricant") instead of falling through to the generic input-
	// missing line. Returns null when the last modifier succeeded or wasn't run.
	string? GetLastModifierFailReason() => null;

	// Recipe-logic accessor - returns the attached RecipeLogic trait.
	Trait.RecipeLogic GetRecipeLogic();

	// === Recipe search input ================================================

	// Verbatim port of upstream's `getRecipeType()` - returns the
	// GTRecipeType the machine processes. RecipeLogic calls
	// `getRecipeType().SearchRecipe(this, filter)` for its candidate
	// iterator.
	GTRecipeType GetRecipeType();

	// Whether `recipe` should appear in this machine's in-machine recipe
	// browser. Default: every recipe of the machine's recipe type. The steam
	// boilers share the STEAM_BOILER recipe type but each runs only a subset
	// (solid -> item fuels, liquid -> fluid fuels, solar -> none), so they
	// override this so the browser shows only what the machine can run.
	bool ShowsInRecipeBrowser(GTRecipe recipe) => true;

	// === RecipeLookup trie ==================================================
	// A holder opts into GTRecipeType's RecipeLookup trie by returning true
	// here and exposing its input-handler contents below. A holder that does
	// NOT (the default) is served by the flat per-station recipe scan instead
	// - correct, just unoptimised. The trie keys recipes by their input
	// item / fluid / circuit ingredients so a search walks only plausibly-
	// matching recipes (see RecipeDB / RecipeLookupCompiler).
	bool SupportsRecipeLookup => false;

	// The machine's INPUT item-handler contents - the items the trie may use
	// as available recipe inputs. Default empty.
	IReadOnlyList<Terraria.Item> LookupInputItems => System.Array.Empty<Terraria.Item>();

	// The machine's INPUT fluid-tank contents. Default empty.
	IReadOnlyList<FluidStack> LookupInputFluids => System.Array.Empty<FluidStack>();

	// Recipe-tier voltage cap. Upstream is `getTier()` -> `GTValues.V[tier]`.
	long RecipeVoltageCap { get; }

	// Position-staggered tick counter. Upstream is `getMachine().getOffsetTimer()`
	// - a per-machine offset so a wall of machines doesn't all scan the
	// recipe registry on the same frame.
	long OffsetTimer { get; }

	// === Energy buffer (for brownout check + drain in HandleRecipeWorking) =
	long EnergyStored { get; set; }

	// === Sound side-channel =================================================
	// Upstream: `getMachine().getLevel().playSound(...)` + per-trait
	// AutoReleasedSound handle. Sound is rendering-side; the trait only
	// tells the machine when to play/stop.
	bool ShouldWorkingPlaySound() => true;
	void EnsureLoopSound(Vector2 worldPos);
	void StopLoopSound();
	void PlayFinishSound(Vector2 worldPos);
	Vector2 GetWorldPos();

	// === Active EU/t - display only =========================================
	// Cache of the running recipe's real (post-overclock) EU/t. Set by
	// RecipeLogic.SetupRecipe; consumed by the UI EU/t label. The actual
	// per-tick EU drain reads the recipe's tickInputs directly.
	long ActiveEut { get; set; }

	// Stable id for the active recipe, used for save-game rebind on load.
	// Documented adaptation: GTRecipe instances aren't NBT-friendly, so
	// we serialize the id and rebind from RecipeRegistry on first post-load
	// tick. Upstream serializes the full GTRecipe.
	string? LastRecipeId { get; set; }

	// === Recipe I/O hooks - substitute for upstream's capability proxy =====
	// Methods return ActionResult so RecipeLogic can read the failing
	// capability + io (only the EU-IN combo triggers brownout backoff).
	// Items and fluids are passed separately because that's all we have at
	// the recipe-payload level today (ItemRecipeCapability + FluidRecipeCapability).

	// Pass 1 of recipe match: verify every input ingredient is present in
	// input slots/tanks. No state mutation.
	//
	// The recipe is passed in so the multiblock dispatcher can read (and lock
	// in) `recipe.GroupColor` for dye-cover-keyed handler grouping - see
	// `WorkableMultiblockMachine.DispatchContents`. Single-machine
	// implementations (WorkableTieredMachine) ignore it.
	ActionResult TryMatchInputContents(
		Api.Recipe.GTRecipe recipe,
		IReadOnlyList<Api.Recipe.Content.Content> items,
		IReadOnlyList<Api.Recipe.Content.Content> fluids);

	// Pass 2 of recipe match: verify every GUARANTEED output (chance ==
	// maxChance) fits. Probabilistic outputs are rolled on completion.
	ActionResult HasOutputRoomContents(
		Api.Recipe.GTRecipe recipe,
		IReadOnlyList<Api.Recipe.Content.Content> items,
		IReadOnlyList<Api.Recipe.Content.Content> fluids);

	// Atomic consume: if every input is fully drainable, commit and return
	// SUCCESS; otherwise return FAIL WITHOUT mutating state.
	//
	// Tool ingredients (Content.Chance == 0, MaxChance > 0) must be PRESENT
	// but are NOT consumed - they stay in the slot for the next cycle.
	ActionResult TryConsumeInputContents(
		Api.Recipe.GTRecipe recipe,
		IReadOnlyList<Api.Recipe.Content.Content> items,
		IReadOnlyList<Api.Recipe.Content.Content> fluids);

	// Best-effort deposit on recipe finish. Probabilistic outputs are
	// rolled against the trait's chance accumulator before deposit; the
	// trait is passed in so the machine can call `logic.RollChance(content)`.
	ActionResult DepositOutputContents(
		Api.Recipe.GTRecipe recipe,
		IReadOnlyList<Api.Recipe.Content.Content> items,
		IReadOnlyList<Api.Recipe.Content.Content> fluids,
		Trait.RecipeLogic logic);

	// EU-input gate. Upstream's `RecipeRunner` runs for ALL capabilities
	// including EU; we keep a dedicated method for it because EU has a few
	// machine-side checks (brown-out detection, charger-slot equalisation)
	// that aren't part of the generic handler walk. Multiblock implementations
	// still route through their group-aware dispatcher internally for parity
	// with upstream - see `WorkableMultiblockMachine.HandleEUThroughCapProxy`.
	ActionResult TryDrainEU(Api.Recipe.GTRecipe recipe, long voltage);

	// EU-output deposit (generator side). Mirrors upstream
	// NotifiableEnergyContainer.handleRecipeInner(IO.OUT, ...) - credits the
	// recipe's per-tick OutputEUt to the machine's buffer. Returns FAIL when
	// the buffer can't accept the full voltage, so RecipeLogic transitions
	// to WAITING (canVoidRecipeOutputs(EU)=false per upstream
	// SimpleGeneratorMachine, so we don't drop the EU).
	ActionResult DepositOutputEU(Api.Recipe.GTRecipe recipe, long voltage);

	// Tick-input handling for the CWU (computation) capability. Upstream
	// dispatches every tick capability generically through RecipeRunner over
	// recipe.tickInputs; our split surface handles EU/item/fluid explicitly via
	// the methods above, so the CWU capability needs its own hook to reach the
	// computation handler. Default no-op SUCCESS - only computation-consuming
	// multis (research station) have a CWU handler in their capability proxy.
	//   - simulate=true  : match-time availability check (won't start without CWU).
	//   - simulate=false : consumes CWU AND performs the duration_is_total_cwu
	//     progress substitution inside NotifiableComputationContainer.HandleRecipeInner.
	ActionResult TryHandleTickCwu(Api.Recipe.GTRecipe recipe, Api.Capability.Recipe.IO io, bool simulate)
		=> ActionResult.SUCCESS;

	// === Unified recipe handling (Phase 1 of recipe-IO surface parity) ======
	// Upstream's RecipeHelper.handleRecipe(holder, recipe, io, contents, ...,
	// isTick, simulate) dispatches EVERY capability in `contents` through ONE
	// capability-keyed walk (RecipeRunner). This method is that single entry point.
	//
	// TRANSITIONAL SHIM (default below): re-splits the capability-keyed content
	// map back into the legacy per-cap hooks (TryMatchInputContents / TryDrainEU /
	// TryHandleTickCwu / ...) so single-block + steam machines keep their EXACT
	// current behavior while RecipeLogic moves onto the unified surface.
	// Multiblocks OVERRIDE this with the real group-aware dispatch
	// (WorkableMultiblockMachine.DispatchContents over the full content map - one
	// call, all caps together, fixing the bus-distinctness-across-caps split).
	// PHASE 2 builds the real capability proxy for single/steam and DELETES this
	// default + the seven legacy hooks above.
	ActionResult HandleRecipe(
		Api.Recipe.GTRecipe recipe,
		Api.Capability.Recipe.IO io,
		IReadOnlyDictionary<object, List<Api.Recipe.Content.Content>> contents,
		bool isTick, bool simulate, Trait.RecipeLogic logic)
	{
		var empty  = (IReadOnlyList<Api.Recipe.Content.Content>)System.Array.Empty<Api.Recipe.Content.Content>();
		var items  = contents.TryGetValue(Api.Capability.Recipe.ItemRecipeCapability.CAP,  out var it) ? it : empty;
		var fluids = contents.TryGetValue(Api.Capability.Recipe.FluidRecipeCapability.CAP, out var fl) ? fl : empty;
		bool hasEu  = contents.ContainsKey(Api.Capability.Recipe.EURecipeCapability.CAP);
		bool hasCwu = contents.ContainsKey(Api.Capability.Recipe.CWURecipeCapability.CAP);

		if (io == Api.Capability.Recipe.IO.IN)
		{
			long voltage = recipe.InputEUt.Voltage;
			if (simulate)
			{
				// EU is a tick input (hasEu only true for the tickInputs map) - a
				// match-time availability check, NOT a drain, exactly as the legacy
				// MatchTickRecipe did.
				if (hasEu && voltage > 0 && EnergyStored < voltage)
					return ActionResult.Fail("gtceu.recipe.insufficient_eu", Api.Capability.Recipe.EURecipeCapability.CAP, Api.Capability.Recipe.IO.IN);
				var r = TryMatchInputContents(recipe, items, fluids);
				if (!r.IsSuccess) return r;
				return hasCwu ? TryHandleTickCwu(recipe, Api.Capability.Recipe.IO.IN, simulate: true) : ActionResult.SUCCESS;
			}
			else
			{
				if (hasEu && voltage > 0)
				{
					var drain = TryDrainEU(recipe, voltage);
					if (!drain.IsSuccess) return drain;
				}
				var c = TryConsumeInputContents(recipe, items, fluids);
				if (!c.IsSuccess) return c;
				return hasCwu ? TryHandleTickCwu(recipe, Api.Capability.Recipe.IO.IN, simulate: false) : ActionResult.SUCCESS;
			}
		}
		else // OUT
		{
			if (simulate)
				return HasOutputRoomContents(recipe, items, fluids);
			var d = DepositOutputContents(recipe, items, fluids, logic);
			if (!d.IsSuccess) return d;
			long outV = recipe.OutputEUt.Voltage;
			return (hasEu && outV > 0) ? DepositOutputEU(recipe, outV) : ActionResult.SUCCESS;
		}
	}
}

// Status enum mirroring upstream's nested RecipeLogic.Status. Lives at the
// top level because C# nested-in-interface enums are awkward to forward-
// declare to IRecipeLogicMachine.NotifyStatusChanged.
public enum RecipeLogicStatus
{
	IDLE,
	WORKING,
	WAITING,
	SUSPEND,
}
