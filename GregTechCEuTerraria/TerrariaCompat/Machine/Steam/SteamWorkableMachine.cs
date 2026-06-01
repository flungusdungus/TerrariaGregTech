#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;
// Alias reads like upstream Java (IO.IN / IO.OUT).
using RecipeIO = GregTechCEuTerraria.Api.Capability.Recipe.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

// 1:1 port of SteamWorkableMachine. Singleblock steam machine + RecipeLogic.
// Steam production state machine runs in parallel on SteamBoilerMachine.
//
// Adaptations: CapabilitiesProxy/Flat replaced with trait walks at IO time
// (Route*); outputFacing dropped (no facing); cleanroom not attached.
// EnergyStored/TryDrainEU route through protected virtual cores - boilers
// keep no-op defaults; SimpleSteamMachine overrides to pay recipe EU from
// the steam tank.
public abstract class SteamWorkableMachine : SteamMachine, IRecipeLogicMachine
{
	protected SteamWorkableMachine() : base() { }
	protected SteamWorkableMachine(bool isHighPressure) : base(isHighPressure) { }

	public abstract GTRecipeType GetRecipeType();

	// Boiler subclasses override - they share STEAM_BOILER recipes but each
	// burns only a subset.
	public virtual bool ShowsInRecipeBrowser(GTRecipe recipe) => true;

	private RecipeLogic? _recipeLogic;
	public RecipeLogic Recipe { get { EnsureRecipeLogic(); return _recipeLogic!; } }

	protected override void EnsureSteamTraits()
	{
		base.EnsureSteamTraits();
		EnsureRecipeLogic();
	}

	protected void EnsureRecipeLogic()
	{
		if (_recipeLogic is not null) return;
		_recipeLogic = new RecipeLogic();
		Traits.Attach(_recipeLogic);
		Traits.RegisterPersistent("RecipeLogic", _recipeLogic);
	}

	public RecipeLogic GetRecipeLogic() { EnsureRecipeLogic(); return _recipeLogic!; }

	// Verbatim onLoad's handlerList.subscribe(recipeLogic::updateTickSubscription).
	// Without this RecipeLogic stays idle when player inserts fuel/water.
	private bool _recipeWakeWired;
	private void WireRecipeLogicWakeOnce()
	{
		if (_recipeWakeWired || _recipeLogic is null) return;
		_recipeWakeWired = true;
		foreach (var t in Traits.AllTraits)
		{
			if (t is NotifiableItemStackHandler ih) ih.AddChangedListener(_recipeLogic.UpdateTickSubscription);
			else if (t is NotifiableFluidTank ft)   ft.AddChangedListener(_recipeLogic.UpdateTickSubscription);
		}
	}

	long IRecipeLogicMachine.EnergyStored { get => EnergyStoredCore; set { } }
	protected virtual long EnergyStoredCore => 0;

	long IRecipeLogicMachine.RecipeVoltageCap => long.MaxValue;

	long IRecipeLogicMachine.OffsetTimer =>
		Main.GameUpdateCount + (uint)(Position.X * 7 + Position.Y * 13);

	bool IRecipeLogicMachine.SupportsRecipeLookup => true;

	IReadOnlyList<Item> IRecipeLogicMachine.LookupInputItems
	{
		get
		{
			List<Item>? items = null;
			foreach (var t in Traits.AllTraits)
				if (t is NotifiableItemStackHandler handler && handler.HandlerIO == RecipeIO.IN)
					(items ??= new List<Item>()).AddRange(handler.Storage.Stacks);
			return items ?? (IReadOnlyList<Item>)Array.Empty<Item>();
		}
	}

	IReadOnlyList<FluidStack> IRecipeLogicMachine.LookupInputFluids
	{
		get
		{
			List<FluidStack>? fluids = null;
			foreach (var t in Traits.AllTraits)
			{
				if (t is not NotifiableFluidTank handler || handler.HandlerIO != RecipeIO.IN) continue;
				foreach (var storage in handler.Storages)
					(fluids ??= new List<FluidStack>()).Add(storage.Fluid);
			}
			return fluids ?? (IReadOnlyList<FluidStack>)Array.Empty<FluidStack>();
		}
	}

	private long _activeEut;
	long IRecipeLogicMachine.ActiveEut { get => _activeEut; set => _activeEut = value; }

	private string? _lastRecipeId;
	string? IRecipeLogicMachine.LastRecipeId { get => _lastRecipeId; set => _lastRecipeId = value; }

	public virtual bool BeforeWorking(GTRecipe recipe) => true;
	public virtual bool OnWorking()                    => true;
	public virtual void AfterWorking()                 { }
	public virtual void OnWaiting()                    { }
	public virtual bool KeepSubscribing()              => false;
	public virtual bool RegressWhenWaiting()    => true;
	public virtual bool AlwaysTryModifyRecipe() => true;
	public virtual bool IsMultiblockController()       => false;
	public virtual bool PreventPowerFail()             => HasPowerFailPreventingCover();
	public virtual GTRecipe? FullModifyRecipe(GTRecipe recipe) => recipe;

	public override bool IsActive => _recipeLogic?.IsActive() ?? false;

	public virtual void NotifyStatusChanged(RecipeLogicStatus oldStatus, RecipeLogicStatus newStatus)
	{
		if (newStatus == RecipeLogicStatus.WORKING)
			((IRecipeLogicMachine)this).EnsureLoopSound(((IRecipeLogicMachine)this).GetWorldPos());
		else
			((IRecipeLogicMachine)this).StopLoopSound();
	}

	// Restart the loop sound for a boiler that loaded mid-recipe - the WORKING
	// status comes back via trait load with no SetStatus transition, so
	// NotifyStatusChanged never fires. EnsureLoopSound is idempotent.
	protected override void OnMachineLoaded()
	{
		base.OnMachineLoaded();
		if (Recipe.IsWorking())
			((IRecipeLogicMachine)this).EnsureLoopSound(((IRecipeLogicMachine)this).GetWorldPos());
	}

	// Steam machines don't voltage-overclock. RecipeLogic.SetupRecipe uses the
	// recipe duration directly; ActiveEut resolves to 0 (no EU contents).
	// SteamBoilerMachine's HP variant applies its 0.5x duration through a
	// FullModifyRecipe override.

	// === IO routing =========================================================
	// Trait-walker approach: scan Traits.AllTraits for handlers matching IO +
	// content capability (Ingredient for items, FluidIngredient for fluids).
	// Upstream uses precomputed capabilitiesProxy maps; equivalent dispatch
	// at IO call time. Subclasses attach traits and the walker picks them up
	// automatically - no need to override hooks per-trait.

	ActionResult IRecipeLogicMachine.TryMatchInputContents(
		Api.Recipe.GTRecipe recipe,
		IReadOnlyList<Api.Recipe.Content.Content> items,
		IReadOnlyList<Api.Recipe.Content.Content> fluids)
	{
		var (itemMatch, _) = Api.Recipe.RecipeContentSplit.Split(
			ItemRecipeCapability.CAP, items, RecipeIO.IN, isTick: false, recipe,
			chanceCache: null, totalRuns: recipe.GetTotalRuns());
		var itemR  = RouteItemPayloads(itemMatch, RecipeIO.IN, simulate: true);
		if (itemR is not null && itemR.Count > 0)
			return ActionResult.Fail("gtceu.recipe.no_input", ItemRecipeCapability.CAP, RecipeIO.IN);
		var (fluidMatch, _) = Api.Recipe.RecipeContentSplit.Split(
			FluidRecipeCapability.CAP, fluids, RecipeIO.IN, isTick: false, recipe,
			chanceCache: null, totalRuns: recipe.GetTotalRuns());
		var fluidR = RouteFluidPayloads(fluidMatch, RecipeIO.IN, simulate: true);
		if (fluidR is not null && fluidR.Count > 0)
			return ActionResult.Fail("gtceu.recipe.no_fluid", FluidRecipeCapability.CAP, RecipeIO.IN);
		return ActionResult.SUCCESS;
	}

	ActionResult IRecipeLogicMachine.HasOutputRoomContents(
		Api.Recipe.GTRecipe recipe,
		IReadOnlyList<Api.Recipe.Content.Content> items,
		IReadOnlyList<Api.Recipe.Content.Content> fluids)
	{
		var (_, itemConsume) = Api.Recipe.RecipeContentSplit.Split(
			ItemRecipeCapability.CAP, items, RecipeIO.OUT, isTick: false, recipe,
			chanceCache: null, totalRuns: recipe.GetTotalRuns());
		var itemR  = RouteItemPayloads(itemConsume, RecipeIO.OUT, simulate: true);
		if (itemR is not null && itemR.Count > 0)
			return ActionResult.Fail("gtceu.recipe.output_full", ItemRecipeCapability.CAP, RecipeIO.OUT);
		var (_, fluidConsume) = Api.Recipe.RecipeContentSplit.Split(
			FluidRecipeCapability.CAP, fluids, RecipeIO.OUT, isTick: false, recipe,
			chanceCache: null, totalRuns: recipe.GetTotalRuns());
		var fluidR = RouteFluidPayloads(fluidConsume, RecipeIO.OUT, simulate: true);
		if (fluidR is not null && fluidR.Count > 0)
			return ActionResult.Fail("gtceu.recipe.fluid_output_full", FluidRecipeCapability.CAP, RecipeIO.OUT);
		return ActionResult.SUCCESS;
	}

	ActionResult IRecipeLogicMachine.TryConsumeInputContents(
		Api.Recipe.GTRecipe recipe,
		IReadOnlyList<Api.Recipe.Content.Content> items,
		IReadOnlyList<Api.Recipe.Content.Content> fluids)
	{
		var (itemMatch, itemConsume) = Api.Recipe.RecipeContentSplit.Split(
			ItemRecipeCapability.CAP, items, RecipeIO.IN, isTick: false, recipe,
			chanceCache: null, totalRuns: recipe.GetTotalRuns());
		var (fluidMatch, fluidConsume) = Api.Recipe.RecipeContentSplit.Split(
			FluidRecipeCapability.CAP, fluids, RecipeIO.IN, isTick: false, recipe,
			chanceCache: null, totalRuns: recipe.GetTotalRuns());

		var simI  = RouteItemPayloads(itemMatch,  RecipeIO.IN, simulate: true);
		if (simI  is not null && simI.Count  > 0)
			return ActionResult.Fail("gtceu.recipe.no_input", ItemRecipeCapability.CAP, RecipeIO.IN);
		var simF = RouteFluidPayloads(fluidMatch, RecipeIO.IN, simulate: true);
		if (simF is not null && simF.Count > 0)
			return ActionResult.Fail("gtceu.recipe.no_fluid", FluidRecipeCapability.CAP, RecipeIO.IN);
		RouteItemPayloads(itemConsume,   RecipeIO.IN, simulate: false);
		RouteFluidPayloads(fluidConsume, RecipeIO.IN, simulate: false);
		return ActionResult.SUCCESS;
	}

	ActionResult IRecipeLogicMachine.DepositOutputContents(
		Api.Recipe.GTRecipe recipe,
		IReadOnlyList<Api.Recipe.Content.Content> items,
		IReadOnlyList<Api.Recipe.Content.Content> fluids,
		RecipeLogic logic)
	{
		var (_, itemConsume) = Api.Recipe.RecipeContentSplit.Split(
			ItemRecipeCapability.CAP, items, RecipeIO.OUT, isTick: false, recipe,
			chanceCache: null, totalRuns: recipe.GetTotalRuns());
		var (_, fluidConsume) = Api.Recipe.RecipeContentSplit.Split(
			FluidRecipeCapability.CAP, fluids, RecipeIO.OUT, isTick: false, recipe,
			chanceCache: null, totalRuns: recipe.GetTotalRuns());
		RouteItemPayloads(itemConsume,   RecipeIO.OUT, simulate: false);
		RouteFluidPayloads(fluidConsume, RecipeIO.OUT, simulate: false);
		return ActionResult.SUCCESS;
	}

	ActionResult IRecipeLogicMachine.TryDrainEU(Api.Recipe.GTRecipe recipe, long voltage) => TryDrainEUCore(voltage);
	// SimpleSteamMachine overrides to pay via SteamEnergyRecipeHandler.
	protected virtual ActionResult TryDrainEUCore(long voltage) => ActionResult.SUCCESS;

	ActionResult IRecipeLogicMachine.DepositOutputEU(Api.Recipe.GTRecipe recipe, long voltage) => ActionResult.SUCCESS;

	private List<Ingredient>? RouteItemPayloads(
		IReadOnlyList<object> payloads, IO io, bool simulate)
	{
		List<Ingredient>? remaining = null;
		foreach (var p in payloads)
		{
			var ing = (Ingredient)p;
			var inner = PeelToInner(ing);
			if (io == RecipeIO.IN && inner.IsEmpty)
				return new List<Ingredient> { ing };
			remaining ??= new List<Ingredient>(payloads.Count);
			remaining.Add(CopyIngredient(ing, CountOf(ing)));
		}
		if (remaining is null || remaining.Count == 0) return null;

		foreach (var t in Traits.AllTraits)
		{
			if (t is not NotifiableItemStackHandler handler) continue;
			if (handler.HandlerIO != io) continue;
			var result = handler.HandleRecipeInner(io, _sentinelRecipe, remaining, simulate);
			if (result is null || result.Count == 0) return null;
			remaining = result;
		}
		return remaining;
	}

	private List<FluidIngredient>? RouteFluidPayloads(
		IReadOnlyList<object> payloads, IO io, bool simulate)
	{
		List<FluidIngredient>? remaining = null;
		foreach (var p in payloads)
		{
			remaining ??= new List<FluidIngredient>(payloads.Count);
			remaining.Add(CopyFluidIngredient((FluidIngredient)p));
		}
		if (remaining is null || remaining.Count == 0) return null;

		foreach (var t in Traits.AllTraits)
		{
			if (t is not NotifiableFluidTank handler) continue;
			if (handler.HandlerIO != io) continue;
			var result = handler.HandleRecipeInner(io, _sentinelRecipe, remaining, simulate);
			if (result is null || result.Count == 0) return null;
			remaining = result;
		}
		return remaining;
	}

	private static readonly GTRecipe _sentinelRecipe = new(
		GTRecipeType.GetOrCreate("__sentinel__"),
		new Dictionary<object, List<Api.Recipe.Content.Content>>(),
		new Dictionary<object, List<Api.Recipe.Content.Content>>(),
		new Dictionary<object, List<Api.Recipe.Content.Content>>(),
		new Dictionary<object, List<Api.Recipe.Content.Content>>(),
		new Dictionary<object, Api.Recipe.Chance.Logic.ChanceLogic>(),
		new Dictionary<object, Api.Recipe.Chance.Logic.ChanceLogic>(),
		new Dictionary<object, Api.Recipe.Chance.Logic.ChanceLogic>(),
		new Dictionary<object, Api.Recipe.Chance.Logic.ChanceLogic>(),
		new List<RecipeCondition>(),
		Array.Empty<object>(),
		new TagCompound(),
		0,
		Api.Recipe.Category.GTRecipeCategory.DEFAULT,
		-1);

	private static Ingredient CopyIngredient(Ingredient ing, int count) => ing switch
	{
		SizedIngredient sized     => SizedIngredient.Create(sized.Inner, count),
		IntProviderIngredient ipi => ipi,
		_                         => count > 1 ? SizedIngredient.Create(ing, count) : ing,
	};

	private static FluidIngredient CopyFluidIngredient(FluidIngredient ing)
	{
		if (ing.ExactType is not null) return new FluidIngredient(ing.ExactType, ing.Amount);
		if (ing.TagName is not null)   return new FluidIngredient(ing.TagName, ing.GetFluids(), ing.Amount);
		if (ing.Attribute is not null) return new FluidIngredient(ing.Attribute, ing.GetFluids(), ing.Amount);
		return ing;
	}

	private static Ingredient PeelToInner(Ingredient ing) => ing switch
	{
		SizedIngredient sized          => PeelToInner(sized.Inner),
		IntProviderIngredient ipi      => PeelToInner(ipi.Inner),
		IntProviderFluidIngredient ipf => ipf.Inner,
		_                              => ing,
	};

	private static int CountOf(Ingredient ing) => ing switch
	{
		SizedIngredient sized     => sized.Amount,
		IntProviderIngredient ipi => ipi.RollSampledCount(),
		_                         => 1,
	};

	private ReLogic.Utilities.SlotId _loopSlot;
	private MachineAudioTracker? _loopTracker;

	Vector2 IRecipeLogicMachine.GetWorldPos() => new(Position.X * 16, Position.Y * 16);

	void IRecipeLogicMachine.EnsureLoopSound(Vector2 worldPos)
	{
		if (Terraria.Audio.SoundEngine.TryGetActiveSound(_loopSlot, out var existing) && existing is not null)
			return;

		var style = StationSounds.TryGetLoop(GetRecipeType().RegistryName);
		if (style is null) return;

		// Tracker uses explicit flag; see WorkableTieredMachine.EnsureLoopSound.
		_loopTracker = new MachineAudioTracker(this);
		var tracker = _loopTracker;
		_loopSlot = Terraria.Audio.SoundEngine.PlaySound(style.Value, worldPos, tracker.Tick);
	}

	void IRecipeLogicMachine.StopLoopSound()
	{
		_loopTracker?.MarkStopped();
		_loopTracker = null;
		if (Terraria.Audio.SoundEngine.TryGetActiveSound(_loopSlot, out var sound) && sound is not null)
			sound.Stop();
		_loopSlot = ReLogic.Utilities.SlotId.Invalid;
	}

	void IRecipeLogicMachine.PlayFinishSound(Vector2 worldPos)
	{
		Terraria.Audio.SoundEngine.PlaySound(StationSounds.DefaultFinish, worldPos);
	}

	internal override void OnClientSync()
	{
		base.OnClientSync();
		EnsureRecipeLogic();
		var rl = (IRecipeLogicMachine)this;
		if (_recipeLogic!.IsWorking()) rl.EnsureLoopSound(rl.GetWorldPos());
		else                           rl.StopLoopSound();
	}

	// Field-only reads (no EnsureRecipeLogic) - reached from MetaMachineTile.ModifyLight
	// on FastParallel worker threads. See WorkableTieredMachine for the pattern rationale.
	int IWorkable.GetProgress()    => _recipeLogic?.GetProgress() ?? 0;
	int IWorkable.GetMaxProgress() => _recipeLogic?.GetMaxProgress() ?? 0;
	bool IWorkable.IsActive()      => _recipeLogic?.IsActive() ?? false;
	bool IControllable.IsWorkingEnabled() => _recipeLogic?.IsWorkingEnabled() ?? true;
	void IControllable.SetWorkingEnabled(bool v) { EnsureRecipeLogic(); _recipeLogic!.SetWorkingEnabled(v); }

	public bool IsRunning      => _recipeLogic?.IsWorking() ?? false;
	public int  ProgressTicks  => _recipeLogic?.GetProgress() ?? 0;
	public int  DurationTicks  => _recipeLogic?.GetMaxProgress() ?? 0;
	public float Progress01    => DurationTicks > 0 ? (float)ProgressTicks / DurationTicks : 0f;

	protected override void OnTick()
	{
		EnsureSteamTraits();
		WireRecipeLogicWakeOnce();
	}

	public override void OnKill()
	{
		base.OnKill();
		((IRecipeLogicMachine)this).StopLoopSound();
	}

	public override void SaveData(TagCompound tag)
	{
		EnsureSteamTraits();
		base.SaveData(tag);
		tag["activeEut"] = _activeEut;
		if (_lastRecipeId is not null) tag["recipe"] = _lastRecipeId;
	}

	public override void LoadData(TagCompound tag)
	{
		EnsureSteamTraits();
		base.LoadData(tag);
		_activeEut       = tag.GetLong("activeEut");
		_lastRecipeId    = tag.ContainsKey("recipe") ? tag.GetString("recipe") : null;
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add(RecipeStatusText.StatusLine(_recipeLogic, "Burning"));
		RecipeStatusText.AppendFailureDetail(_recipeLogic, lines);
	}
}
