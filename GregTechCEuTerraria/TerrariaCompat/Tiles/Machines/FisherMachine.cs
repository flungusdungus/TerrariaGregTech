#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;  // TagCompound

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// 1:1 port of com.gregtechceu.gtceu.common.machine.electric.FisherMachine.
// Per-tier (LV..LuV) auto-fisher: sits over water, drains EU, consumes bait,
// produces loot every maxProgress ticks into its output cache.
//
// DEVIATIONS:
//   - Loot: upstream rolls MC's BuiltInLootTables.FISHING / FISHING_FISH; we
//     hand-roll biome+Y-aware loot via FishingLootRoller (junk gate mirrors the
//     FISHING vs FISHING_FISH split).
//   - Bait: upstream filters strictly on Items.STRING; we accept gtceu:string
//     OR any Terraria Item.bait > 0 (worm / master bait / ...).
//   - Water check: 5x5 row below, adapted to Terraria liquids.
//   - Work gate runs inline at OnTick top (upstream uses TickableSubscription) -
//     same behavior, no subscription drift.
//
// Energy: receiver-mode container; capacity = V[tier]*64; per-tick draw =
// V[tier-1] (LV draws ULV 8 EU/t, LuV draws IV 8192 EU/t). Charger slot lives
// here (Fisher extends TieredEnergyMachine directly - upstream parity).
public sealed class FisherMachine : TieredEnergyMachine, IWorkable, IControllable
{
	public FisherMachine() { }
	public FisherMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "Fisher";

	// Receiver-mode container (input only).
	public override bool CanAccept => true;
	public override long EnergyCapacity => VoltageTiers.Voltage(Tier) * 64L;

	// energyPerTick = V[tier-1] (java:113).
	private long EnergyPerTick
	{
		get
		{
			int idx = Math.Max(0, (int)Tier - 1);
			return VoltageTiers.Voltage((VoltageTier)idx);
		}
	}

	// (tier+1)^2 output cache (HandlerIO=BOTH, CapabilityIO=OUT = pipe-extract-only).
	private int InventorySize { get { int t = (int)Tier; return (t + 1) * (t + 1); } }

	// upstream calcMaxProgress(tier)
	private static int CalcMaxProgress(int tier) =>
		(int)(800.0 - 170 * ((double)tier - 1.0) + (Math.Max(0, tier - 4) / 0.012));

	public int MaxProgress => CalcMaxProgress((int)Tier);

	private NotifiableItemStackHandler? _cache;
	private NotifiableItemStackHandler? _baitHandler;
	private AutoOutputTrait? _autoOutput;

	public NotifiableItemStackHandler Cache       { get { EnsureTraits(); return _cache!; } }
	public NotifiableItemStackHandler BaitHandler { get { EnsureTraits(); return _baitHandler!; } }

	public override AutoOutputTrait? AutoOutput { get { EnsureTraits(); return _autoOutput; } }

	protected override bool HasChargerSlot => true;

	// gtceu:string item type, resolved once. 0 = not registered.
	private static int _stringItemType = -1;
	private static int StringItemType
	{
		get
		{
			if (_stringItemType < 0)
				_stringItemType = ModLoader.GetMod("GregTechCEuTerraria").TryFind<ModItem>("string", out var s) ? s.Type : 0;
			return _stringItemType;
		}
	}

	// Bait filter: accept gtceu:string (upstream-verbatim) OR any Terraria
	// vanilla bait (`Item.bait > 0` - worms / master bait / fireflies / ...).
	private static bool IsBait(Item item)
	{
		if (item is null || item.IsAir) return false;
		return item.type == StringItemType || item.bait > 0;
	}

	private void EnsureTraits()
	{
		if (_cache is not null) return;
		BindDefinition();

		_cache = new NotifiableItemStackHandler(InventorySize, IO.BOTH, IO.OUT);
		Traits.Attach(_cache);
		Traits.RegisterPersistent("Cache", _cache);

		// 1-slot filtered bait handler (java:116-117).
		_baitHandler = new NotifiableItemStackHandler(1, IO.BOTH, IO.IN).SetFilter(IsBait);
		Traits.Attach(_baitHandler);
		Traits.RegisterPersistent("Bait", _baitHandler);

		// AutoOutputTrait.ofItems(cache) - handler-ref form, no IItemHandler projection.
		_autoOutput = AutoOutputTrait.OfItems(_cache);
		Traits.Attach(_autoOutput);
		Traits.RegisterPersistent("AutoOutput", _autoOutput);

		// Mirror upstream setEnableEnvironmentalExplosions(false) (no-op for us -
		// CheckEnvironment is a stub - but kept for parity).
		var explosion = Traits.GetTrait<EnvironmentalExplosionTrait>(EnvironmentalExplosionTrait.TYPE);
		explosion?.SetEnableEnvironmentalExplosions(false);
	}

	protected override void OnMachineLoaded()
	{
		base.OnMachineLoaded();
		EnsureTraits();
	}

	// Cache -> InventoryOutput, Bait -> InventoryInput; charger via base.
	public override Item[]? GetSlotGroup(SlotGroup group) => group switch
	{
		SlotGroup.InventoryOutput => Cache.Storage.Stacks,
		SlotGroup.InventoryInput  => BaitHandler.Storage.Stacks,
		_                         => base.GetSlotGroup(group),
	};

	public override bool SupportsAutoOutputItems => true;

	private bool _isWorkingEnabled = true;
	public bool IsWorkingEnabled() => _isWorkingEnabled;
	public void SetWorkingEnabled(bool enabled) => _isWorkingEnabled = enabled;

	private bool _junkEnabled = true;
	public bool JunkEnabled
	{
		get => _junkEnabled;
		set => _junkEnabled = value;
	}

	private int _progress;
	private bool _active;

	int IWorkable.GetProgress()    => _progress;
	int IWorkable.GetMaxProgress() => MaxProgress;
	bool IWorkable.IsActive()      => _active;
	public override bool IsActive  => _active;

	public const int WaterCheckSize = 5;  // upstream WATER_CHECK_SIZE
	private bool _hasWater;

	protected override void OnTick()
	{
		EnsureTraits();

		if (!_hasWater || GetMcOffsetTimer() % MaxProgress == 0L)
			UpdateHasWater();

		// updateFishingUpdateSubscription gate (java:179) - energy + bait + enabled.
		// Bait filter runs on Insert, so any non-air bait stack is valid.
		bool canFish = DrainEnergy(simulate: true)
		            && !BaitHandler.Storage.GetStackInSlot(0).IsAir
		            && _isWorkingEnabled;
		if (!canFish)
		{
			if (_active || _progress != 0)
			{
				_active = false;
				_progress = 0;
			}
			return;
		}

		if (!_hasWater)
		{
			_active = false;
			return;
		}

		_active = true;

		// Pay the per-tick EU.
		DrainEnergy(simulate: false);

		if (_progress >= MaxProgress)
		{
			DoFishingRoll();
			_progress = -1;       // upstream: -1 then increment -> 0
		}
		_progress++;
	}

	// DEVIATION from upstream's 5x5-plane all-water requirement (java:191): we just check water below the fisher
	private void UpdateHasWater()
	{
		int left  = Position.X;
		int right = Position.X + Size.Width - 1;
		int baseY = Position.Y + Size.Height;

		for (int dy = 0; dy < WaterCheckSize; dy++)
		{
			int tileY = baseY + dy;
			if (tileY < 0 || tileY >= Main.maxTilesY) break;
			for (int x = left; x <= right; x++)
			{
				if (x < 0 || x >= Main.maxTilesX) continue;
				var t = Main.tile[x, tileY];
				if (t.LiquidAmount > 0 && t.LiquidType == LiquidID.Water)
				{
					_hasWater = true;
					return;
				}
			}
		}
		_hasWater = false;
	}

	// roll loot, deposit into cache, consume bait (java:204-246).
	private void DoFishingRoll()
	{
		int waterCenterX = Position.X + 1;
		int waterY       = Position.Y + Size.Height;

		// One roll per tick; any deposit consumes bait (upstream useBait |= tryFillCache).
		var rolled = FishingLootRoller.Roll(Tier, waterCenterX, waterY, _junkEnabled);
		bool useBait = false;
		if (!rolled.IsAir)
			useBait = TryFillCache(rolled);

		if (useBait)
		{
			// junk-enabled = 1 bait per yield, fish-only = 2.
			int consume = _junkEnabled ? 1 : 2;
			var slot = BaitHandler.Storage.GetStackInSlot(0);
			if (!slot.IsAir)
			{
				int take = Math.Min(slot.stack, consume);
				slot.stack -= take;
				if (slot.stack <= 0) BaitHandler.Storage.SetStackInSlot(0, new Item());
				BaitHandler.OnContentsChanged();
			}
		}
	}

	// upstream tryFillCache - walk slots, any leftover reduction = success.
	private bool TryFillCache(Item stack)
	{
		var storage = Cache.Storage;
		for (int i = 0; i < storage.SlotCount; i++)
		{
			var leftover = storage.Insert(i, stack, simulate: false);
			if (leftover.stack < stack.stack) return true;
		}
		return false;
	}

	// simulate-first energy drain (java:257-265)
	private bool DrainEnergy(bool simulate)
	{
		long resultEnergy = EnergyContainer.EnergyStored - EnergyPerTick;
		if (resultEnergy >= 0L && resultEnergy <= EnergyContainer.EnergyCapacity)
		{
			if (!simulate) EnergyContainer.SetEnergyStored(resultEnergy);
			return true;
		}
		return false;
	}

	// Cache/bait/charger save via Traits; persist the machine-owned state.
	public override void SaveData(TagCompound tag)
	{
		EnsureTraits();
		base.SaveData(tag);   // Energy trait + ChargerSlot via TieredEnergyMachine
		tag["progress"]          = _progress;
		tag["active"]            = _active;
		tag["hasWater"]          = _hasWater;
		tag["isWorkingEnabled"]  = _isWorkingEnabled;
		tag["junkEnabled"]       = _junkEnabled;
	}

	public override void LoadData(TagCompound tag)
	{
		EnsureTraits();
		base.LoadData(tag);
		_progress         = tag.GetInt("progress");
		_active           = tag.GetBool("active");
		_hasWater         = tag.GetBool("hasWater");
		_isWorkingEnabled = !tag.ContainsKey("isWorkingEnabled") || tag.GetBool("isWorkingEnabled");
		_junkEnabled      = !tag.ContainsKey("junkEnabled") || tag.GetBool("junkEnabled");
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add($"Speed: {MaxProgress} ticks / catch");
		lines.Add("Water needed: below the machine");
		lines.Add($"Draw: {EnergyPerTick:N0} EU/t");
		lines.Add($"Fishing Power: {FishingLootRoller.FishingPower(Tier)}");
		lines.Add($"Luck: +{FishingLootRoller.SyntheticLuck(Tier):0.00}");
		if (_active)
			lines.Add($"Progress: {_progress} / {MaxProgress}");
		else if (!_isWorkingEnabled)
			lines.Add("Disabled");
		else if (!_hasWater)
			lines.Add("Idle: no water below");
		else if (BaitHandler.Storage.GetStackInSlot(0).IsAir)
			lines.Add("Idle: no bait (string / worm / etc.)");
		else if (!DrainEnergy(simulate: true))
			lines.Add("Idle: not enough power");
	}
}
