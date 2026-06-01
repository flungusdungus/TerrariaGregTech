#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// 1:1 port of com.gregtechceu.gtceu.common.machine.electric.ItemCollectorMachine.
// Per-tier (LV..EV) auto item-puller: scans dropped items in range, filters,
// inserts into its output cache. Drains EU per active tick.
//
// DEVIATIONS:
//   - Filter lives on the machine (Terraria items have no per-instance NBT),
//     same shape as MagnetItem; matcher/tag math shared via ItemFilterEdit.
//   - Range bbox is a 2D square around the machine centre (upstream 3D).
//   - Direct consume-in-place instead of upstream's velocity-nudge magnet -
//     see CollectItemsInRange for the MP-race rationale.
//   - Work gate runs inline at OnTick top (upstream uses TickableSubscription).
public sealed class ItemCollectorMachine : TieredEnergyMachine, IFilterableMachine
{
	// Upstream INVENTORY_SIZES indexed by VoltageTier (ULV unused, LV..EV registered).
	private static readonly int[] InventorySizes = { 4, 9, 16, 25, 25 };
	private const double MotionMultiplier = 0.04;
	private const int    BaseEuConsumption = 6;

	public ItemCollectorMachine() { }
	public ItemCollectorMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "Item Collector";

	// Receiver-mode container; capacity V[tier]*64.
	public override bool CanAccept => true;
	public override long EnergyCapacity => VoltageTiers.Voltage(Tier) * 64L;

	// BASE_EU_CONSUMPTION * (1 << (tier-1)) - LV 6 / MV 12 / HV 24 / EV 48.
	private long EnergyPerTick
	{
		get
		{
			int t = Math.Max(1, (int)Tier);
			return (long)BaseEuConsumption * (1L << (t - 1));
		}
	}

	// Output cache (HandlerIO=BOTH so the pull can write, CapabilityIO=OUT).
	private int InventorySize
	{
		get
		{
			int t = Math.Clamp((int)Tier, 0, InventorySizes.Length - 1);
			return InventorySizes[t];
		}
	}

	private NotifiableItemStackHandler? _output;
	private AutoOutputTrait? _autoOutput;

	public NotifiableItemStackHandler Output { get { EnsureTraits(); return _output!; } }

	public override AutoOutputTrait? AutoOutput { get { EnsureTraits(); return _autoOutput; } }

	protected override bool HasChargerSlot => true;

	private void EnsureTraits()
	{
		if (_output is not null) return;
		BindDefinition();

		_output = new NotifiableItemStackHandler(InventorySize, IO.BOTH, IO.OUT);
		Traits.Attach(_output);
		Traits.RegisterPersistent("Output", _output);

		// AutoOutputTrait.ofItems(output) - handler-ref form.
		_autoOutput = AutoOutputTrait.OfItems(_output);
		Traits.Attach(_autoOutput);
		Traits.RegisterPersistent("AutoOutput", _autoOutput);

		// Mirror upstream setEnableEnvironmentalExplosions(false).
		var explosion = Traits.GetTrait<EnvironmentalExplosionTrait>(EnvironmentalExplosionTrait.TYPE);
		explosion?.SetEnableEnvironmentalExplosions(false);
	}

	protected override void OnMachineLoaded()
	{
		base.OnMachineLoaded();
		EnsureTraits();
	}

	public override Item[]? GetSlotGroup(SlotGroup group) => group switch
	{
		SlotGroup.InventoryOutput => Output.Storage.Stacks,
		_                         => base.GetSlotGroup(group),
	};

	public override bool SupportsAutoOutputItems => true;

	// Per-machine filter (MagnetItem storage shape); edits route through the
	// server-authoritative MachineFilterAction.
	private int _filterOrdinal;                  // 0 = simple/items, 1 = tag
	private SimpleItemFilter _simpleFilter = new();
	private TagItemFilter    _tagFilter    = new();

	public int FilterOrdinal
	{
		get => _filterOrdinal;
		set => _filterOrdinal = Math.Clamp(value, 0, 1);
	}
	public SimpleItemFilter SimpleFilter => _simpleFilter;
	public TagItemFilter    TagFilter    => _tagFilter;

	private IItemFilter ActiveFilter() => _filterOrdinal == 1 ? (IItemFilter)_tagFilter : _simpleFilter;

	// MaxRange = 2^(tier+2) - LV 8 / MV 16 / HV 32 / EV 64. Player-adjustable down to 1.
	public int MaxRange
	{
		get
		{
			int t = (int)Tier;
			return (int)Math.Pow(2, t + 2);
		}
	}

	private int _range = -1;
	public int Range
	{
		get { if (_range < 0) _range = MaxRange; return _range; }
		set => _range = Math.Clamp(value, 1, MaxRange);
	}

	private bool _isWorkingEnabled = true;
	public bool IsWorkingEnabled() => _isWorkingEnabled;
	public void SetWorkingEnabled(bool enabled) => _isWorkingEnabled = enabled;

	private bool _active;
	public override bool IsActive => _active;

	// Drain gate (upstream drainEnergy(true) && isWorkingEnabled).
	protected override void OnTick()
	{
		EnsureTraits();

		if (!_isWorkingEnabled || !DrainEnergy(simulate: true))
		{
			_active = false;
			return;
		}

		_active = true;

		// Pay the per-tick EU (upstream pays once per `update()` tick).
		DrainEnergy(simulate: false);

		CollectItemsInRange();
	}

	// DEVIATION: server-authoritative direct consume-in-place,
	// replacing upstream's velocity-nudge magnet. The magnet races each client's
	// local player-magnet pickup in MP (items snatched mid-flight / pingpong on
	// sync). Here OnTick is server-only, scan + filter + insert + SyncItem; items
	// never move, no magnet race.
	private void CollectItemsInRange()
	{
		var filter = ActiveFilter();

		// Machine centre in pixels (footprint visual centre).
		float cx = (Position.X + Size.Width  * 0.5f) * 16f;
		float cy = (Position.Y + Size.Height * 0.5f) * 16f;

		float rangePixels = Range * 16f;

		// Consumed-tile coords for the sparkle effect, batched into one packet/tick.
		List<Microsoft.Xna.Framework.Point>? collectedAt = null;

		for (int i = 0; i < Main.maxItems; i++)
		{
			Item it = Main.item[i];
			if (it is null || !it.active || it.IsAir) continue;

			// Square-bbox range gate (cheap reject; upstream's bbox is also a box).
			float dx = cx - it.Center.X;
			float dy = cy - it.Center.Y;
			if (Math.Abs(dx) > rangePixels || Math.Abs(dy) > rangePixels) continue;

			if (!filter.Test(it)) continue;

			// Insert what fits; leave the rest in place.
			int before = it.stack;
			var effectAt = new Microsoft.Xna.Framework.Point(
				(int)(it.Center.X / 16f), (int)(it.Center.Y / 16f));
			var rem = TryFillOutput(it);
			if (rem.IsAir || rem.stack <= 0)
			{
				Main.item[i].active = false;
			}
			else if (rem.stack < before)
			{
				it.stack = rem.stack;
			}
			else
			{
				// No room - skip without broadcasting.
				continue;
			}
			(collectedAt ??= new()).Add(effectAt);
			if (Main.netMode == Terraria.ID.NetmodeID.Server)
				Terraria.NetMessage.SendData(Terraria.ID.MessageID.SyncItem, -1, -1, null, i);
		}

		// Sparkle: PlayLocal for SP/host, Send for remote clients (Dust no-ops
		// on a dedicated server). Same convention as BlockExplosionEffectPacket.
		if (collectedAt is { Count: > 0 })
		{
			foreach (var pt in collectedAt)
				Net.ItemCollectEffectPacket.PlayLocal(pt.X, pt.Y);
			if (Main.netMode == Terraria.ID.NetmodeID.Server)
				Net.ItemCollectEffectPacket.Send(collectedAt);
		}
	}

	// Insert into the first accepting slot; returns the remainder.
	private Item TryFillOutput(Item stack)
	{
		var s = Output.Storage;
		Item remainder = stack.Clone();
		for (int i = 0; i < s.SlotCount; i++)
		{
			remainder = s.Insert(i, remainder, simulate: false);
			if (remainder.IsAir || remainder.stack <= 0) return remainder;
		}
		return remainder;
	}

	// simulate-first drain
	private bool DrainEnergy(bool simulate)
	{
		long want = EnergyPerTick;
		long stored = EnergyContainer.EnergyStored;
		long resultEnergy = stored - want;
		if (resultEnergy >= 0L && resultEnergy <= EnergyContainer.EnergyCapacity)
		{
			if (!simulate) EnergyContainer.SetEnergyStored(resultEnergy);
			return true;
		}
		return false;
	}

	public override void SaveData(TagCompound tag)
	{
		EnsureTraits();
		base.SaveData(tag);   // energy + charger via TieredEnergyMachine
		tag["range"]            = Range;
		tag["active"]           = _active;
		tag["isWorkingEnabled"] = _isWorkingEnabled;
		tag["filterOrdinal"]    = _filterOrdinal;
		var s = _simpleFilter.SaveFilter();
		if (s != null) tag["simple"] = s;
		var t = _tagFilter.SaveFilter();
		if (t != null) tag["tag"] = t;
	}

	public override void LoadData(TagCompound tag)
	{
		EnsureTraits();
		base.LoadData(tag);
		if (tag.ContainsKey("range")) _range = Math.Clamp(tag.GetInt("range"), 1, MaxRange);
		_active            = tag.GetBool("active");
		_isWorkingEnabled  = !tag.ContainsKey("isWorkingEnabled") || tag.GetBool("isWorkingEnabled");
		_filterOrdinal     = Math.Clamp(tag.GetInt("filterOrdinal"), 0, 1);
		_simpleFilter = tag.ContainsKey("simple")
			? SimpleItemFilter.LoadFilter(tag.GetCompound("simple")) : new SimpleItemFilter();
		_tagFilter = tag.ContainsKey("tag")
			? TagItemFilter.LoadFilter(tag.GetCompound("tag")) : new TagItemFilter();
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add($"Range: {Range} / {MaxRange} tiles");
		lines.Add($"Draw: {EnergyPerTick:N0} EU/t");
		lines.Add($"Filter: {(_filterOrdinal == 1 ? "Tags" : "Items")}");
		if (!_isWorkingEnabled) lines.Add("Disabled");
		else if (_active)        lines.Add("Active");
	}
}
