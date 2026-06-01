#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Feature;
using Status = GregTechCEuTerraria.Api.Machine.Feature.RecipeLogicStatus;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// PORTED - verbatim port of
// com.gregtechceu.gtceu.api.machine.trait.RecipeLogic.
//
// Field-for-field, method-for-method mirror of upstream's RecipeLogic.java.
// A side-by-side diff against the upstream file should produce only the
// documented adaptations below.
//
// Documented adaptations (everything else is verbatim):
//   - Component -> string (Terraria has no Component system; tooltip strings
//     render plain).
//   - ChanceCacheMap (per-capability IdentityHashMap) -> flat Dictionary<string,
//     int> keyed by ChanceKey(Ingredient). Per-capability dispatch isn't
//     needed yet because we only have ItemRecipeCapability + FluidRecipeCapability
//     at the recipe-payload level and ChanceKey produces distinct keys for
//     each.
//   - sync_system annotations (@SaveField, @SyncToClient, ClientFieldChangeListener)
//     dropped - MachineStateSyncPacket carries the trait's Save() blob;
//     client-side fields update on packet receive. updateSound + scheduleRenderUpdate
//     callbacks are dropped (sound is machine-side via IRecipeLogicMachine).
//   - RecipeHelper.matchContents / handleRecipeIO / matchTickRecipe collapsed
//     into IRecipeLogicMachine.TryMatchInputContents / TryConsumeInputContents
//     / HasOutputRoomContents / DepositOutputContents. Items and fluids are
//     passed as separate args. ActionResult preserved so EU brownout
//     detection works precisely (gates on io == IN && capability == EU).
//   - MultiblockControllerCover detection (for preventPowerFail) collapsed
//     to IRecipeLogicMachine.PreventPowerFail - the machine-side flag, which
//     WorkableTieredMachine backs with a MachineControllerCover cover walk.
//   - GTRecipe save/load via id (LastRecipeId on IRecipeLogicMachine).
//     Upstream serializes the full GTRecipe blob; we resolve from
//     RecipeRegistry on first post-load tick.
//   - regressRecipe gate moved INTO regressRecipe per upstream (was inside
//     handleRecipeWorking in PMT's earlier port - drift).
//   - IFancyTooltip NOT implemented (it's a tML-GUI type in TerrariaCompat; an
//     Api type can't depend on it). The reason data is still exposed via
//     GetWaitingReason() / GetFailureReasons(), surfaced on the machine hover
//     by RecipeStatusText.StatusLine / .AppendFailureDetail.
public class RecipeLogic : MachineTrait, IWorkable
{
	public static readonly MachineTraitType<RecipeLogic> TYPE = new(allowMultipleInstances: false);
	public override MachineTraitType TraitType => TYPE;

	// Recipe status uses the top-level `RecipeLogicStatus` enum. Upstream
	// nests this inside RecipeLogic; we alias via `using Status = ...` at
	// file scope so trait-internal callsites read identically to upstream
	// (`Status.WORKING` etc) while `IRecipeLogicMachine.NotifyStatusChanged`
	// can keep its public top-level enum type with no cast.

	// === Mutable state (verbatim upstream field-for-field) ==================

	public List<GTRecipe>? lastFailedMatches;

	private Status _status = Status.IDLE;
	public Status GetStatus() => _status;

	protected bool _isActive;

	protected string? _waitingReason;
	public string? GetWaitingReason() => _waitingReason;

	protected readonly List<string> _failureReasons = new();
	public IReadOnlyList<string> GetFailureReasons() => _failureReasons;

	protected readonly Dictionary<GTRecipe, string> _failureReasonMap = new();
	public IReadOnlyDictionary<GTRecipe, string> GetFailureReasonMap() => _failureReasonMap;

	protected GTRecipe? _lastRecipe;
	public GTRecipe? GetLastRecipe() => _lastRecipe;

	protected int _consecutiveRecipes = 0;
	public int GetConsecutiveRecipes() => _consecutiveRecipes;

	protected GTRecipe? _lastOriginRecipe;
	public GTRecipe? GetLastOriginRecipe() => _lastOriginRecipe;

	protected int _progress;
	public int GetProgress() => _progress;

	protected int _duration;

	protected bool _recipeDirty;
	public bool IsRecipeDirty() => _recipeDirty;

	protected long _totalContinuousRunningTime;
	public long GetTotalContinuousRunningTime() => _totalContinuousRunningTime;

	protected int _runAttempt = 0;
	protected int _runDelay   = 0;

	protected bool _suspendAfterFinish = false;
	public bool IsSuspendAfterFinish() => _suspendAfterFinish;
	public void SetSuspendAfterFinish(bool v) => _suspendAfterFinish = v;

	// Chance accumulator - flat Dictionary<string, int> keyed by
	// ChanceKey(Ingredient). See class-level note about per-cap collapse.
	protected readonly Dictionary<string, int> _chanceCaches = new();
	public IReadOnlyDictionary<string, int> GetChanceCaches() => _chanceCaches;
	private static readonly Random Rng = new();

	protected TickableSubscription? _subscription;

	// Active-sound handle - machine-side (the trait only tells the machine
	// when to start / stop / play-finish).
	protected object? _workingSound;

	// === Construction / attachment ==========================================

	public RecipeLogic() : base() { }

	public IRecipeLogicMachine GetRLMachine() => (IRecipeLogicMachine)Machine;

	protected override IReadOnlyList<Type> ValidMachineClasses() =>
		new[] { typeof(IRecipeLogicMachine) };

	// === resetRecipeLogic (line 171) ========================================
	// Verbatim from upstream - abort current cycle, drop to IDLE unless
	// already SUSPEND. The resyncAllFields() call is dropped (sync system
	// adaptation - MachineStateSyncPacket re-sends the full trait blob).
	public void ResetRecipeLogic()
	{
		_recipeDirty = false;
		_lastRecipe = null;
		_lastOriginRecipe = null;
		_consecutiveRecipes = 0;
		_progress = 0;
		_duration = 0;
		_isActive = false;
		lastFailedMatches = null;
		_waitingReason = null;
		_failureReasons.Clear();
		if (_status != Status.SUSPEND)
			SetStatus(Status.IDLE);
		UpdateTickSubscription();
	}

	// === onMachineLoad (line 190) ===========================================
	public override void OnMachineLoad()
	{
		base.OnMachineLoad();
		TryRestoreLastRecipe();
		UpdateTickSubscription();
	}

	// Re-attach the running recipe object from its persisted id after a load.
	// Only LastRecipeId round-trips (not the GTRecipe), so a machine loaded
	// mid-recipe has _lastRecipe == null and would restart at progress 0 once it
	// resumes ticking. Done here (one-shot, right after LoadData) rather than
	// only in ServerTick because a multi loads with IsFormed=true + UNINIT_ERROR,
	// so its recipe logic isn't subscribed to ServerTick until the structure
	// re-walks - by which point a transient invalidation may already have wiped
	// the state. Resolving here keeps _lastRecipe set across that window.
	public void TryRestoreLastRecipe()
	{
		if (_lastRecipe != null) return;
		if (!(IsWorking() || IsWaiting())) return;
		var m = GetRLMachine();
		var rid = m.LastRecipeId;
		if (!string.IsNullOrEmpty(rid))
			_lastRecipe = m.GetRecipeType()?.GetRecipeById(rid!);
	}

	// === updateTickSubscription (line 195) ==================================
	public void UpdateTickSubscription()
	{
		if (IsSuspend() || !GetRLMachine().IsRecipeLogicAvailable())
		{
			if (_subscription is not null)
			{
				_subscription.Unsubscribe();
				_subscription = null;
			}
		}
		else
		{
			_subscription = SubscribeServerTick(_subscription, ServerTick);
		}
	}

	// === setProgress (line 206) =============================================
	public void SetProgress(int progress) { _progress = progress; }

	// === getProgressPercent (line 211) ======================================
	public double GetProgressPercent() => _duration == 0 ? 0.0 : _progress / (_duration * 1.0);

	// === serverTick (line 222) - verbatim ===================================
	// `virtual` so CleanroomLogic (and any future custom recipe-logic) can
	// replace the cycle. Mirrors upstream where CleanroomLogic.serverTick
	// shadows RecipeLogic.serverTick via subclass override.
	public virtual void ServerTick()
	{
		// Gate the per-tick recipe-work clock to upstream's 20 Hz cadence.
		// Without this, every per-tick site below (_progress++, _runDelay--,
		// HandleRecipeWorking's EU drain, FindAndHandleRecipe retry) runs at
		// Terraria's 60 Hz and recipes finish 3x wall-clock too fast - and EU /
		// steam drain 3x per real second, breaking the upstream steam economy
		// (one max-temp LP boiler can't keep up with one LP steam macerator at
		// 60 Hz draw, even though upstream balances it at +40 mB/sec surplus).
		// FromMcTicks(1) = 3 at SimulationSpeed=1.0; the user's SimulationSpeed
		// config multiplier scales this naturally.
		if (Main.GameUpdateCount % (uint)global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(1) != 0) return;

		var machine = GetRLMachine();

		// NOTE: re-attaching _lastRecipe from the persisted LastRecipeId after a
		// world load is handled once in OnMachineLoad (TryRestoreLastRecipe),
		// which fires from MetaMachine.EnsureLoaded on the first machine tick -
		// before this ServerTick can run for any machine (recipe logic only
		// subscribes after EnsureLoaded). So _lastRecipe is already resolved here
		// for a machine loaded mid-recipe; no re-resolution needed. This keeps
		// ServerTick verbatim with upstream RecipeLogic.serverTick.

		if (!IsSuspend())
		{
			if (!IsIdle() && _lastRecipe != null)
			{
				if (_progress < _duration)
				{
					if (_runDelay > 0)
					{
						_runDelay--;
					}
					else
					{
						HandleRecipeWorking();
					}
				}
				if (_progress >= _duration)
				{
					OnRecipeFinish();
				}
			}
			else if (_lastRecipe != null)
			{
				FindAndHandleRecipe();
			}
			else if (!machine.KeepSubscribing() || machine.OffsetTimer % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(5) == 0)
			{
				FindAndHandleRecipe();
				if (lastFailedMatches != null)
				{
					foreach (var match in lastFailedMatches)
					{
						if (CheckMatchedRecipeAvailable(match)) break;
					}
				}
			}
		}
		bool unsubscribe = false;
		if (IsSuspend())
		{
			// Machine is paused and can unsubscribe.
			unsubscribe = true;
		}
		else if (_lastRecipe == null && IsIdle() && !machine.KeepSubscribing() && !_recipeDirty &&
		         lastFailedMatches == null)
		{
			// No recipes available and the machine wants to unsubscribe until notified.
			unsubscribe = true;
		}
		if (IsIdle())
		{
			_failureReasons.Clear();
			_failureReasons.AddRange(_failureReasonMap.Values);
		}
		if (unsubscribe && _subscription != null)
		{
			_subscription.Unsubscribe();
			_subscription = null;
		}
	}

	// === matchRecipe (line 265) =============================================
	// Upstream: `RecipeHelper.matchContents(getRLMachine(), recipe)` =
	// `matchRecipe(holder, recipe) && matchTickRecipe(holder, recipe)`. Our
	// adaptation: split per-cap items / fluids on the machine surface, then
	// AND the three ActionResults. First failing branch wins for
	// capability+io attribution. The tick branch gates per-tick EU
	// availability - without it a recipe starts on an empty energy buffer,
	// runs one tick, immediately drops to WAITING.
	// Step D (recipe-IO unification): routes through the single
	// `RecipeHelper.MatchContents` entry (regular-IN match -> regular-OUT room ->
	// tick-IN feasibility) instead of the old per-capability fan-out. Behaviour
	// is preserved by the `IRecipeLogicMachine.HandleRecipe` shim, which re-splits
	// the content map back onto the same TryMatchInputContents / HasOutputRoom
	// Contents / TryDrainEU / TryHandleTickCwu hooks. Multiblocks override
	// HandleRecipe to dispatch the whole map at once (Step C).
	protected virtual ActionResult MatchRecipe(GTRecipe recipe)
		=> RecipeHelper.MatchContents(GetRLMachine(), recipe);

	// === checkRecipe (line 269) =============================================
	protected ActionResult CheckRecipe(GTRecipe recipe)
	{
		var conditionResult = CheckConditions(recipe);
		if (!conditionResult.IsSuccess) return conditionResult;

		// Voltage cap - circuit ingredient is matched the standard way (Test()
		// against IntCircuitItem in any attached input handler, including the
		// machine's CircuitInventory), mirroring upstream IntCircuitIngredient.
		long voltageCap = GetRLMachine().RecipeVoltageCap;
		if (recipe.InputEUt.Voltage > voltageCap)
			return ActionResult.Fail("gtceu.recipe.eu_too_high", EURecipeCapability.CAP, IO.IN);

		return MatchRecipe(recipe);
	}

	// Walks recipe.Conditions calling Test(this). Returns first failure.
	// Mirrors upstream RecipeHelper.checkConditions inline.
	// Failure reason embeds the condition's tooltip text after a ':' separator,
	// so RecipeStatusText.Resolve can surface the specific requirement
	// ("Requires cleanroom: cleanroom") instead of the generic
	// "Conditions not met". Falls back to bare key when the condition has no
	// tooltip.
	protected ActionResult CheckConditions(GTRecipe recipe)
	{
		foreach (var condition in recipe.Conditions)
		{
			if (!condition.Test(this))
			{
				string baseKey = $"gtceu.recipe.condition.{condition.GetTypeName()}";
				string msg = condition.GetFailureMessage(this);
				string reason = string.IsNullOrEmpty(msg) ? baseKey : $"{baseKey}|{msg}";
				return new ActionResult(false, reason, null, null);
			}
		}
		return ActionResult.SUCCESS;
	}

	// Strip count wrappers (SizedIngredient / IntProviderIngredient) down to
	// the matching ingredient - used by ChanceKey.
	private static Ingredient PeelToInner(Ingredient ing) => ing switch
	{
		SizedIngredient sized          => PeelToInner(sized.Inner),
		IntProviderIngredient ipi      => PeelToInner(ipi.Inner),
		IntProviderFluidIngredient ipf => ipf.Inner,
		_                              => ing,
	};


	// === checkMatchedRecipeAvailable (line 276) =============================
	public virtual bool CheckMatchedRecipeAvailable(GTRecipe match)
	{
		// Deviation from upstream: pre-screen the raw recipe against the
		// machine's inputs BEFORE running FullModifyRecipe. Otherwise modifier-
		// side cancellations (insufficient_voltage / coil_temperature_too_low
		// / wrong_machine_type - see GTRecipeModifiers) record their reason
		// for every too-high-V candidate while the input bus is empty, and an
		// idle multi surfaces "Voltage Tier Too Low" on hover even though the
		// player hasn't put anything in. Upstream displays this noise via JEI
		// recipe lookup so it's less visible; our world-hover failure list is
		// the only surfacing, so the noise is loud.
		//
		// The raw shape is a close-enough proxy for "could this recipe ever
		// run": modifier may rescale inputs (parallel), but if the player has
		// none of the ingredient, no scale of it will match either. A recipe
		// that survives the raw match goes through the full modifier path as
		// before, so genuine modifier-side failures still surface.
		var rawMatch = MatchRecipe(match);
		if (!rawMatch.IsSuccess)
		{
			PutFailureReason(this, match, rawMatch.ReasonText());
			return false;
		}

		var modified = GetRLMachine().FullModifyRecipe(match);
		if (modified != null)
		{
			var recipeMatch = CheckRecipe(modified);
			if (recipeMatch.IsSuccess)
			{
				SetupRecipe(modified);
			}
			else
			{
				PutFailureReason(this, match, recipeMatch.ReasonText());
			}
			if (_lastRecipe != null && GetStatus() == Status.WORKING)
			{
				_lastOriginRecipe = match;
				lastFailedMatches = null;
				return true;
			}
		}
		else
		{
			// Modifier-side cancellation (`ModifierFunction.Cancel(reason)` or
			// `ModifierFunction.NULL`) - capture the reason key so the player
			// sees a useful waiting reason ("No rotor installed", "Out of
			// lubricant") instead of the recipe silently dropping. Falls back
			// to the modifier's DEFAULT_FAILURE key when the modifier didn't
			// specify one (= legacy `ModifierFunction.NULL` callers).
			var reason = GetRLMachine().GetLastModifierFailReason()
				?? ModifierFunction.DEFAULT_FAILURE;
			PutFailureReason(this, match, reason);
		}
		return false;
	}

	// === handleRecipeWorking (line 294) - verbatim ==========================
	public void HandleRecipeWorking()
	{
		// upstream: `assert lastRecipe != null;` - we'd NRE on null deref
		// below anyway, so the assert is implicit.
		var conditionResult = CheckConditions(_lastRecipe!);
		if (conditionResult.IsSuccess)
		{
			var handleTick = HandleTickRecipe(_lastRecipe!);
			if (handleTick.IsSuccess)
			{
				SetStatus(Status.WORKING);
				if (!GetRLMachine().OnWorking())
				{
					InterruptRecipe();
					return;
				}
				_progress++;
				_totalContinuousRunningTime++;
			}
			else
			{
				SetWaiting(handleTick.ReasonText());

				// Machine isn't getting enough power - suspend after 5 attempts.
				if (handleTick.Io == IO.IN && ReferenceEquals(handleTick.Capability, EURecipeCapability.CAP))
				{
					_runAttempt++;
					_runAttempt = Math.Clamp(_runAttempt, 0, 5);
					if (_runAttempt == 5)
					{
						// Upstream walks `getMachine().getCoverContainer().getCovers()` for
						// MachineControllerCover with preventPowerFail(). Collapsed to
						// IRecipeLogicMachine.PreventPowerFail - WorkableTieredMachine backs
						// it with exactly that cover walk.
						bool preventPowerFail = GetRLMachine().PreventPowerFail();
						if (GetRLMachine().IsMultiblockController() && !preventPowerFail)
						{
							_runAttempt = 0;
							SetStatus(Status.SUSPEND);
						}
					}
					_runDelay = _runAttempt * 60;
				}
			}
		}
		else
		{
			SetWaiting(conditionResult.ReasonText());
		}
		if (IsWaiting() || IsSuspend())
		{
			RegressRecipe();
		}
	}

	// === regressRecipe (line 344) - verbatim ================================
	// Gate is INSIDE the method (upstream).
	protected void RegressRecipe()
	{
		if (_progress > 0 && GetRLMachine().RegressWhenWaiting())
		{
			_progress = 1;
		}
	}

	// === searchRecipe (line 350) - verbatim =================================
	public IEnumerator<GTRecipe> SearchRecipe()
	{
		return GetRLMachine().GetRecipeType().SearchRecipe(GetRLMachine(), _ => true).GetEnumerator();
	}

	// === findAndHandleRecipe (line 354) - verbatim ==========================
	public void FindAndHandleRecipe()
	{
		lastFailedMatches = null;

		// Try to execute last recipe if possible.
		if (!_recipeDirty && _lastRecipe != null && CheckRecipe(_lastRecipe).IsSuccess)
		{
			GTRecipe recipe = _lastRecipe;
			_lastRecipe = null;
			_lastOriginRecipe = null;
			SetupRecipe(recipe);
		}
		else
		{
			// Try to find and handle a new recipe.
			_failureReasonMap.Clear();
			_lastRecipe = null;
			_lastOriginRecipe = null;
			HandleSearchingRecipes(SearchRecipe());
		}
		_recipeDirty = false;
	}

	// === handleSearchingRecipes (line 373) - verbatim =======================
	protected void HandleSearchingRecipes(IEnumerator<GTRecipe> matches)
	{
		while (matches.MoveNext())
		{
			GTRecipe match = matches.Current;

			// If a new recipe was found, cache found recipe.
			if (CheckMatchedRecipeAvailable(match))
				return;

			if (!MatchRecipe(match).IsSuccess)
			{
				continue;
			}

			// Cache matching recipes.
			lastFailedMatches ??= new List<GTRecipe>();
			lastFailedMatches.Add(match);
		}
	}

	// === handleTickRecipe (line 393) - verbatim =============================
	public ActionResult HandleTickRecipe(GTRecipe recipe)
	{
		if (!recipe.HasTick()) return ActionResult.SUCCESS;

		var result = MatchTickRecipe(recipe);
		if (!result.IsSuccess) return result;

		result = HandleTickRecipeIO(recipe, IO.IN);
		if (!result.IsSuccess) return result;

		result = HandleTickRecipeIO(recipe, IO.OUT);
		return result;
	}

	// matchTickRecipe - adaptation of RecipeHelper.matchTickRecipe. Splits
	// into EU drain (via TryDrainEU on machine) + per-cap match.
	// Step D: the tick-IN feasibility check (EU-available gate + tick item/fluid
	// match + CWU/t simulate), now via the unified HandleRecipe(IN, tick, sim).
	// Through the shim this is identical to the old inline split. Still called
	// separately from HandleTickRecipe (the per-tick working path).
	protected ActionResult MatchTickRecipe(GTRecipe recipe)
		=> RecipeHelper.HandleRecipe(GetRLMachine(), recipe, IO.IN, isTick: true, simulate: true);

	// handleTickRecipeIO - adaptation of RecipeHelper.handleTickRecipeIO.
	// Step D: per-tick IO (consume tick inputs + EU drain + CWU on IN; deposit
	// tick outputs + emit OutputEUt on OUT), via the unified HandleRecipe(io,
	// tick, consume). Shim-equivalent to the old inline split.
	protected virtual ActionResult HandleTickRecipeIO(GTRecipe recipe, IO io)
		=> RecipeHelper.HandleRecipe(GetRLMachine(), recipe, io, isTick: true, simulate: false);

	// === setupRecipe (line 406) - verbatim ==================================
	// virtual: upstream's method is non-final; LargeBoilerRecipeLogic overrides
	// it to rescale `_duration` by current throttle after the base setup.
	public virtual void SetupRecipe(GTRecipe recipe)
	{
		if (!GetRLMachine().BeforeWorking(recipe))
		{
			SetStatus(Status.IDLE);
			_consecutiveRecipes = 0;
			_progress = 0;
			_duration = 0;
			_isActive = false;
			return;
		}
		var handledIO = HandleRecipeIO(recipe, IO.IN);
		if (handledIO.IsSuccess)
		{
			if (_lastRecipe != null && !recipe.Equals(_lastRecipe))
			{
				_chanceCaches.Clear();
			}
			_failureReasonMap.Clear();
			_recipeDirty = false;
			_lastRecipe = recipe;
			SetStatus(Status.WORKING);
			_progress = 0;
			// Verbatim upstream: `duration = recipe.duration`. The recipe
			// reaching setupRecipe is already overclocked / paralleled -
			// IRecipeLogicMachine.FullModifyRecipe ran the RecipeModifier
			// (overclock chain) in checkMatchedRecipeAvailable.
			_duration = recipe.Duration;
			// Adaptation: ActiveEut is a machine-side display value (the UI
			// EU/t label). Upstream has no equivalent - per-tick EU is read
			// from the recipe's tickInputs each tick. We cache the post-modifier
			// real EU/t so the UI matches actual consumption.
			GetRLMachine().ActiveEut = RecipeHelper.GetRealEUt(recipe).GetTotalEU();
			_isActive = true;
			// Adaptation: persist recipe id for post-load rebind (upstream
			// serializes the full GTRecipe object).
			GetRLMachine().LastRecipeId = recipe.Id;
		}
	}

	// === handleRecipeIO (line 569) - Step D unified path ====================
	// Regular (non-tick) IO: consume regular inputs on IN, deposit regular
	// outputs on OUT, via the unified HandleRecipe(io, regular, consume).
	// Shim-equivalent to the old per-cap split.
	protected virtual ActionResult HandleRecipeIO(GTRecipe recipe, IO io)
		=> RecipeHelper.HandleRecipe(GetRLMachine(), recipe, io, isTick: false, simulate: false);

	// === setStatus (line 430) - verbatim ====================================
	public void SetStatus(Status status)
	{
		if (_status != status)
		{
			if (_status == Status.WORKING)
			{
				_totalContinuousRunningTime = 0;
			}
			if ((status == Status.WAITING || status == Status.SUSPEND) && _suspendAfterFinish)
			{
				status = Status.SUSPEND;
				_suspendAfterFinish = false;
			}
			GetRLMachine().NotifyStatusChanged(
				_status, status);
			_status = status;
			UpdateTickSubscription();
			if (_status != Status.WAITING)
			{
				_waitingReason = null;
			}
		}
	}

	// === setWaiting (line 450) - verbatim ===================================
	public void SetWaiting(string? reason)
	{
		SetStatus(Status.WAITING);
		_waitingReason = reason;
		GetRLMachine().OnWaiting();
	}

	// === markLastRecipeDirty (line 460) - verbatim ==========================
	public void MarkLastRecipeDirty() => _recipeDirty = true;

	// === Status predicates (lines 464-484) - verbatim =======================
	public bool IsWorking() => _status == Status.WORKING;
	public bool IsIdle()    => _status == Status.IDLE;
	public bool IsWaiting() => _status == Status.WAITING;
	public bool IsSuspend() => _status == Status.SUSPEND;

	public bool IsWorkingEnabled() => !IsSuspend() && !IsSuspendAfterFinish();

	// === setWorkingEnabled (line 485) - verbatim ============================
	public void SetWorkingEnabled(bool isWorkingAllowed)
	{
		if (!isWorkingAllowed && GetStatus() == Status.IDLE)
		{
			SetStatus(Status.SUSPEND);
		}
		else
		{
			SetSuspendAfterFinish(!isWorkingAllowed);
			if (isWorkingAllowed)
			{
				if (_lastRecipe != null && _duration > 0)
				{
					SetStatus(Status.WORKING);
				}
				else
				{
					SetStatus(Status.IDLE);
				}
			}
		}
	}

	// === getMaxProgress (line 501) - verbatim ===============================
	public int GetMaxProgress() => _duration;

	// === isActive (line 505) - verbatim =====================================
	public bool IsActive() => IsWorking() || IsWaiting() || (IsSuspend() && _isActive);

	// === hasCustomProgressLine + getCustomProgressLine (lines 509-522) ======
	public virtual bool HasCustomProgressLine() => false;
	public virtual string? GetCustomProgressLine() => null;

	// === onRecipeFinish (line 524) - verbatim ===============================
	public void OnRecipeFinish()
	{
		GetRLMachine().AfterWorking();
		if (_lastRecipe != null)
		{
			_runAttempt = 0;
			_runDelay = 0;
			_consecutiveRecipes++;
			HandleRecipeIO(_lastRecipe, IO.OUT);
			// No sound here - verbatim with upstream onRecipeFinish. The machine
			// loop sound is governed solely by NotifyStatusChanged (WORKING
			// starts it, any other status stops it); a per-recipe-finish cue was
			// tried and removed (too noisy on fast machines, and not upstream).
			// Don't ready the next recipe after finish if suspend is set
			// so that the modifiers won't be applied until re-starting.
			if (_suspendAfterFinish)
			{
				SetStatus(Status.SUSPEND);
				_consecutiveRecipes = 0;
				_progress = 0;
				_duration = 0;
				_isActive = false;
				// Force a recipe recheck.
				_lastRecipe = null;
				GetRLMachine().LastRecipeId = null;
				return;
			}
			if (GetRLMachine().AlwaysTryModifyRecipe())
			{
				if (_lastOriginRecipe != null)
				{
					var modified = GetRLMachine().FullModifyRecipe(_lastOriginRecipe.Copy());
					if (modified == null)
					{
						MarkLastRecipeDirty();
					}
					else
					{
						_lastRecipe = modified;
					}
				}
				else
				{
					MarkLastRecipeDirty();
				}
			}
			// Try it again.
			var recipeCheck = CheckRecipe(_lastRecipe!);
			if (!_recipeDirty && recipeCheck.IsSuccess)
			{
				SetupRecipe(_lastRecipe!);
			}
			else
			{
				SetStatus(Status.IDLE);
				_consecutiveRecipes = 0;
				_progress = 0;
				_duration = 0;
				_isActive = false;
			}
		}
	}

	// === interruptRecipe (line 580) - verbatim ==============================
	public void InterruptRecipe()
	{
		GetRLMachine().AfterWorking();
		if (_lastRecipe != null)
		{
			SetStatus(Status.IDLE);
			_progress = 0;
			_duration = 0;
		}
	}

	// === Chance roll - mirror of upstream ChanceLogic.OR ====================
	//
	// Upstream ChanceLogic.OR (ChanceLogic.java:42-69):
	//     cached  = previously stored "leftover chance" (random initial)
	//     chance  = newChance + cached
	//     while (chance >= maxChance) { produce one; chance -= maxChance;
	//                                   newChance -= maxChance; }
	//     cache[key] = newChance/2 + cached
	//
	// Makes probabilistic outputs deterministic over time. The flat key
	// space (vs upstream's per-capability IdentityHashMap) is documented
	// at the class level.
	public bool RollChance(Api.Recipe.Content.Content content)
	{
		int max = content.MaxChance;
		if (max <= 0 || content.Chance >= max) return true;
		int newChance = content.Chance;
		string key = ChanceKey((Ingredient)content.Payload);
		if (!_chanceCaches.TryGetValue(key, out int cached))
			cached = Rng.Next(max);
		int chance = newChance + cached;
		bool produced = false;
		if (chance >= max)
		{
			produced = true;
			newChance -= max;
		}
		_chanceCaches[key] = newChance / 2 + cached;
		return produced;
	}

	public static string ChanceKey(Ingredient ing) => PeelToInner(ing) switch
	{
		ItemStackIngredient isi      => $"item:{(string.IsNullOrEmpty(isi.UpstreamId) ? isi.ItemType.ToString() : isi.UpstreamId)}",
		TagIngredient tag            => $"tag:{tag.TagName}",
		NBTPredicateIngredient nbt   => $"nbt:{nbt.UpstreamId}:{nbt.ItemType}",
		IntCircuitIngredient ic      => $"circuit:{ic.Configuration}",
		FluidIngredient fi           => fi.ExactType is not null ? $"fluid:{fi.ExactType.Id}"
		                              : fi.TagName  is not null ? $"fluidtag:{fi.TagName}"
		                              : fi.Attribute is not null ? $"fluidattr:{fi.Attribute.Id}"
		                              : "fluid:?",
		_                            => $"?:{ing.GetTypeName()}",
	};

	// === Failure-reason tracking (line 702/708) - verbatim ==================

	public static void PutFailureReason(object machine, GTRecipe recipe, string reason)
	{
		if (machine is IRecipeLogicMachine rlm)
			PutFailureReason(rlm.GetRecipeLogic(), recipe, reason);
	}

	// Priority ranking for failure reasons - higher = more informative for
	// the player. Used by `Save()` to pick which reason to ship when many
	// recipes fail simultaneously. Ordering:
	//   - `insufficient_out`: inputs DID match; output blocked.        Most actionable.
	//   - `insufficient_eu` / `eu_too_high`: inputs+outputs OK; power. Actionable.
	//   - `recipe_modifier.*`: specific modifier rejection (no rotor, ...). Actionable.
	//   - `recipe.condition.*`: an environmental gate failed.          Actionable.
	//   - `recipe_logic.no_capabilities` / `no_contents`: structural.  Less actionable.
	//   - `recipe_logic.insufficient_in`: most-common noise.           Least actionable.
	//   - everything else: mid-tier default.
	private static int RankFailureReason(string r) => r switch
	{
		"gtceu.recipe_logic.insufficient_out" => 100,
		"gtceu.recipe.insufficient_eu"        => 90,
		"gtceu.recipe.eu_too_high"            => 85,
		_ when r.StartsWith("gtceu.recipe_modifier.", System.StringComparison.Ordinal) => 80,
		_ when r.StartsWith("gtceu.recipe.condition.", System.StringComparison.Ordinal) => 70,
		"gtceu.recipe_logic.no_capabilities"  => 50,
		"gtceu.recipe_logic.no_contents"      => 45,
		"gtceu.recipe_logic.insufficient_in"  => 10,
		_                                     => 30,
	};

	public static void PutFailureReason(RecipeLogic logic, GTRecipe recipe, string reason)
	{
		var map = logic._failureReasonMap;
		// Upstream's `ModifierFunction.DEFAULT_FAILURE` sentinel - our
		// string equivalent uses null/empty: non-default reasons always
		// overwrite, default reasons (null/empty) only fill empty slots.
		if (map.ContainsKey(recipe))
		{
			if (!string.IsNullOrEmpty(reason)) map[recipe] = reason;
		}
		else
		{
			map[recipe] = reason;
		}
	}

	// === Persistence ========================================================
	// Verbatim subset of upstream's @SaveField list. Adaptation:
	//   - lastRecipe / lastOriginRecipe -> LastRecipeId on the machine
	//     (GTRecipe instances aren't NBT-friendly).
	//   - chanceCaches -> flat string->int map (per-cap collapse documented).

	public override void Save(TagCompound tag) => WriteCore(tag, includeTransient: true);

	// Wire-only snapshot. Omits fields that upstream's `@SyncToClient`
	// deliberately does NOT mark dirty during a running recipe:
	//   - progress   : upstream's `progress++` (line 305) skips
	//                  `markClientSyncFieldDirty`. Client interpolates locally
	//                  via OnClientTick + a reset on status transition (Load).
	//   - runAttempt / runDelay : server-internal WAITING bookkeeping.
	//   - totalContinuousRunningTime : monotonic per-tick, server-only display.
	//   - chanceCaches : server-side recipe-search memo, never client-visible.
	// Status / duration / isActive / failureReasons / waitingReason still ride
	// so the client's progress arrow, hover status line and loop sound react
	// to transitions; the dirty-skip in MachineStateSyncPacket fires for the
	// entire running interval between transitions because none of these flip.
	public override void SaveForSync(TagCompound tag) => WriteCore(tag, includeTransient: false);

	private void WriteCore(TagCompound tag, bool includeTransient)
	{
		tag["status"]                      = (byte)_status;
		tag["isActive"]                    = _isActive;
		tag["waitingReason"]               = _waitingReason ?? string.Empty;
		// _failureReasons is transient (rebuilt each search from
		// _failureReasonMap) - upstream doesn't @SaveField it, it carries a
		// separate @SyncToClient. We dropped the per-field sync annotations and
		// sync the whole Save() blob via MachineStateSyncPacket, so this field
		// must ride Save() or the idle failure-reason hover detail never
		// reaches an MP client (the client never runs the recipe search).
		//
		// DEVIATION - DEDUPLICATED + capped at 1
		// distinct entry. Stations like `large_extractor` have 2000+ candidate
		// recipes, ALL of which fail (e.g. `insufficient_in`) with an idle
		// machine - saving the raw list produced a 77KB blob that overflowed
		// Terraria's 65KB packet limit and silently broke state-sync for every
		// machine iterated after it. Both display consumers
		// (RecipeStatusText.AppendFailureDetail + MultiblockDisplayText.AddRecipe
		// FailReasonLine) already dedupe by resolved text and only render the
		// unique set, so saving one canonical entry is display-equivalent.
		if (_failureReasons.Count > 0)
		{
			string? best = null;
			int bestRank = int.MinValue;
			foreach (var r in _failureReasons)
			{
				int rank = RankFailureReason(r);
				if (rank > bestRank) { bestRank = rank; best = r; }
			}
			if (best is not null) tag["failureReasons"] = new List<string> { best };
		}
		tag["consecutiveRecipes"]          = _consecutiveRecipes;
		tag["duration"]                    = _duration;
		tag["suspendAfterFinish"]          = _suspendAfterFinish;
		tag["recipeDirty"]                 = _recipeDirty;

		if (includeTransient)
		{
			tag["progress"]                    = _progress;
			tag["totalContinuousRunningTime"]  = _totalContinuousRunningTime;
			tag["runAttempt"]                  = _runAttempt;
			tag["runDelay"]                    = _runDelay;
			if (_chanceCaches.Count > 0)
			{
				var cc = new TagCompound();
				foreach (var (k, v) in _chanceCaches) cc[k] = v;
				tag["chanceCaches"] = cc;
			}
		}
	}

	public override void Load(TagCompound tag)
	{
		// Capture pre-Load values so a wire-side recipe boundary (where
		// SaveForSync omitted "progress") can reset the client's interpolated
		// counter cleanly. Disk load always carries "progress" so this fallback
		// only matters for MP state-sync.
		var prevStatus = _status;
		var prevConsecutive = _consecutiveRecipes;

		if (tag.ContainsKey("status"))                    _status                     = (Status)tag.GetByte("status");
		if (tag.ContainsKey("isActive"))                  _isActive                   = tag.GetBool("isActive");
		if (tag.ContainsKey("waitingReason"))             _waitingReason              = tag.GetString("waitingReason") is var s && s.Length == 0 ? null : s;
		_failureReasons.Clear();
		if (tag.ContainsKey("failureReasons"))            _failureReasons.AddRange(tag.GetList<string>("failureReasons"));
		if (tag.ContainsKey("consecutiveRecipes"))        _consecutiveRecipes         = tag.GetInt("consecutiveRecipes");
		if (tag.ContainsKey("duration"))                  _duration                   = tag.GetInt("duration");
		if (tag.ContainsKey("totalContinuousRunningTime")) _totalContinuousRunningTime = tag.GetLong("totalContinuousRunningTime");
		if (tag.ContainsKey("suspendAfterFinish"))        _suspendAfterFinish         = tag.GetBool("suspendAfterFinish");
		if (tag.ContainsKey("runAttempt"))                _runAttempt                 = tag.GetInt("runAttempt");
		if (tag.ContainsKey("runDelay"))                  _runDelay                   = tag.GetInt("runDelay");
		if (tag.ContainsKey("recipeDirty"))               _recipeDirty                = tag.GetBool("recipeDirty");

		if (tag.ContainsKey("progress"))
		{
			_progress = tag.GetInt("progress");
		}
		else if (prevStatus != _status || _consecutiveRecipes != prevConsecutive)
		{
			// Recipe boundary detected over the wire (SaveForSync omitted
			// "progress"). Two cases:
			//   1. Status transition (IDLE<->WORKING) - new recipe start or end.
			//   2. _consecutiveRecipes changed - back-to-back same-recipe run
			//      where OnRecipeFinish->SetupRecipe stays WORKING and the
			//      status field doesn't flip. Without this branch the client
			//      would freeze at progress=_duration across every chained
			//      cycle until the recipe chain finally breaks.
			_progress = 0;
		}

		_chanceCaches.Clear();
		if (tag.ContainsKey("chanceCaches"))
		{
			var cc = tag.Get<TagCompound>("chanceCaches");
			foreach (var kv in cc)
				if (kv.Value is int i) _chanceCaches[kv.Key] = i;
		}
	}

	// Client-side progress interpolation. SaveForSync omits `_progress`, so
	// during a running recipe the server emits no broadcasts (byte-equal
	// snapshots) and the client must advance progress on its own. Bounded by
	// `_duration` so we never overshoot before the server's WORKING->IDLE
	// transition arrives. Mirrors upstream's `progress++` in the server-side
	// tick loop without going through the dirty-marking setter.
	//
	// Gated to the same FromMcTicks(1) cadence as ServerTick (line 199) -
	// without this the client interpolates at 60 Hz while the server advances
	// at 20 Hz, so the progress arrow races 3x ahead of the real recipe and
	// loops back when the WORKING->IDLE sync finally arrives.
	public override void OnClientTick()
	{
		if (Main.GameUpdateCount % (uint)global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(1) != 0) return;
		if (_status == Status.WORKING && _progress < _duration)
			_progress++;
	}
}
