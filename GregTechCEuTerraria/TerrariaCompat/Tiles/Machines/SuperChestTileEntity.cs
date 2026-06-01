#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Port of com.gregtechceu.gtceu.common.machine.storage.QuantumChestMachine.
// Single-slot huge-capacity item storage - the item mirror of SuperTank.
// Upstream registers the class twice (super_chest low / quantum_chest high);
// we collapse both into one all-tier definition. Toggles (verbatim upstream):
// IsLocked (refuse non-locked types), IsVoiding (accept+discard overflow),
// IsAutoOutput (AutoOutputTrait, IControllable-aliased below).
//
// DEVIATIONS: ItemCache trait collapsed onto the machine (the
// machine IS the single-slot IItemHandler); getItemHandlerCap front-face
// null-out dropped (no facing); markClientSyncFieldDirty dropped
// (MachineStateSyncPacket carries the SaveData blob).
public class SuperChestTileEntity : MetaMachine, IItemHandler, IControllable
{
	public SuperChestTileEntity() { }
	public SuperChestTileEntity(VoltageTier tier) : base(tier) { }

	protected override string  Label       => Definition?.Label ?? "Super Chest";

	// upstream registerQuantumChests: MAX -> long.MaxValue, else 4M * 2^(tier-1)
	// (ULV 2M, doubling per tier).
	internal static long MaxAmountForTier(VoltageTier tier)
	{
		int t = (int)tier;
		if (t >= (int)VoltageTier.MAX) return long.MaxValue;
		return t == 0 ? 2_000_000L : 4_000_000L << (t - 1);
	}

	private long _maxAmount = -1;
	public long MaxAmount
	{
		get
		{
			if (_maxAmount < 0) _maxAmount = MaxAmountForTier(Tier);
			return _maxAmount;
		}
	}

	// _stored carries the item TYPE (stack field unused); _storedAmount (long) is
	// the real count. protected so CreativeChest can swap source type directly.
	protected Item _stored = new();
	protected long _storedAmount;
	private Item _lockedItem = new();

	public bool IsVoiding { get; set; }
	public bool IsLocked => !_lockedItem.IsAir;
	public Item StoredItem => _stored;
	public long StoredAmount => _storedAmount;

	// Type-equality "same item" (Terraria stackables carry no per-stack NBT, so
	// it's sufficient - upstream's GTUtil.isSameItemSameTags).
	private static bool SameItem(Item a, Item b) =>
		!a.IsAir && !b.IsAir && a.type == b.type;

	// Lock filter - upstream ItemCache.filter.
	private bool Accepts(Item stack) => !IsLocked || SameItem(stack, _lockedItem);

	// IItemHandler - single slot 0. virtual hooks for CreativeChest's override.
	public int SlotCount => 1;

	public virtual Item GetSlot(int slot)
	{
		if (_stored.IsAir || _storedAmount <= 0) return new Item();
		var view = _stored.Clone();
		view.stack = (int)Math.Min(_storedAmount, _stored.maxStack);
		return view;
	}

	// upstream ItemCache.insertItem - returns leftover (doesn't mutate input).
	// When voiding, `free` is unbounded so the whole stack reports stored while
	// _storedAmount still clamps - overflow accepted and discarded.
	public virtual Item Insert(int slot, Item item, bool simulate)
	{
		if (item is null || item.IsAir) return new Item();
		long free = IsVoiding ? long.MaxValue : MaxAmount - _storedAmount;
		long canStore = 0;
		if ((_stored.IsAir || SameItem(_stored, item)) && Accepts(item))
			canStore = Math.Min(item.stack, free);

		if (!simulate && canStore > 0)
		{
			if (_stored.IsAir)
			{
				_stored = item.Clone();
				_stored.stack = 1;
			}
			_storedAmount = Math.Min(MaxAmount, _storedAmount + canStore);
		}

		long leftoverCount = item.stack - canStore;
		if (leftoverCount <= 0) return new Item();
		var leftover = item.Clone();
		leftover.stack = (int)leftoverCount;
		return leftover;
	}

	// upstream ItemCache.extractItem
	public virtual Item Extract(int slot, int amount, bool simulate)
	{
		if (_stored.IsAir || _storedAmount <= 0 || amount <= 0) return new Item();
		long toExtract = Math.Min(_storedAmount, amount);
		var copy = _stored.Clone();
		copy.stack = (int)toExtract;
		if (!simulate && toExtract > 0)
		{
			_storedAmount -= toExtract;
			if (_storedAmount == 0) _stored = new Item();
		}
		return copy;
	}

	// Lock filter only - the output-side input gate is AdjacentItemPush's job
	// (side-aware), not a blanket per-machine gate.
	public virtual bool IsItemValid(int slot, Item item) => Accepts(item);

	// upstream AutoOutputTrait.ofItems(cache)
	private AutoOutputTrait? _autoOutput;
	public override AutoOutputTrait? AutoOutput { get { EnsureAutoOutput(); return _autoOutput; } }

	private void EnsureAutoOutput()
	{
		if (_autoOutput is not null) return;
		_autoOutput = AutoOutputTrait.OfItems(slotStart: 0, slotCount: 1);
		Traits.Attach(_autoOutput);
		Traits.RegisterPersistent("AutoOutput", _autoOutput);
	}

	protected override void OnMachineLoaded()
	{
		base.OnMachineLoaded();
		EnsureAutoOutput();
	}

	public override bool SupportsAutoOutputItems  => true;
	public override bool SupportsAutoOutputFluids => false;

	// SuperChestLayout toggle + ChestAction bind here.
	public bool IsAutoOutput
	{
		get => AutoOutput!.IsAutoOutputItems;
		set => AutoOutput!.SetAllowAutoOutputItems(value);
	}

	// IControllable - a chest's "working enabled" IS its item auto-output toggle.
	// Field-only read (see DrumMachine for the FastParallel rationale).
	bool IControllable.IsWorkingEnabled() => _autoOutput?.IsAutoOutputItems ?? false;
	void IControllable.SetWorkingEnabled(bool enabled) => AutoOutput!.SetAllowAutoOutputItems(enabled);

	public override bool SupportsWorkingEnabledToggle => false;

	// Upstream setLocked: snap the locked type to whatever's currently stored.
	public void SetLocked(bool locked)
	{
		if (locked && !_stored.IsAir)
		{
			_lockedItem = _stored.Clone();
			_lockedItem.stack = 1;
		}
		else if (!locked)
		{
			_lockedItem = new Item();
		}
	}

	// Hand one stack of the stored item to a player - the GUI dump button.
	public void DumpStackTo(Player player)
	{
		if (_stored.IsAir || _storedAmount <= 0) return;
		int amount = (int)Math.Min(_storedAmount, _stored.maxStack);
		var taken = Extract(0, amount, simulate: false);
		if (taken.IsAir) return;
		// PlayerGive: dedicated server falls back to a synced world drop; SP
		// inserts directly. One canonical helper, same observable behavior.
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, player.GetSource_OpenItem(taken.type), taken);
	}

	// Portable data across break -> re-place (upstream IDropSaveMachine).
	public override void WritePortableData(TagCompound tag)
	{
		if (_stored.IsAir || _storedAmount <= 0) return;
		tag["stored"]       = ItemIO.Save(_stored);
		tag["storedAmount"] = _storedAmount;
	}

	public override void ReadPortableData(TagCompound tag)
	{
		if (tag.ContainsKey("stored"))
		{
			_stored = ItemIO.Load(tag.GetCompound("stored"));
			_storedAmount = tag.GetLong("storedAmount");
		}
	}

	public override void SaveData(TagCompound tag)
	{
		EnsureAutoOutput();
		base.SaveData(tag);   // Traits.Save -> AutoOutput trait
		if (!_stored.IsAir) tag["stored"] = ItemIO.Save(_stored);
		tag["storedAmount"] = _storedAmount;
		tag["voiding"] = IsVoiding;
		if (!_lockedItem.IsAir) tag["locked"] = ItemIO.Save(_lockedItem);
	}

	public override void LoadData(TagCompound tag)
	{
		EnsureAutoOutput();
		base.LoadData(tag);
		_stored = tag.ContainsKey("stored") ? ItemIO.Load(tag.GetCompound("stored")) : new Item();
		_storedAmount = tag.GetLong("storedAmount");
		IsVoiding = tag.GetBool("voiding");
		_lockedItem = tag.ContainsKey("locked") ? ItemIO.Load(tag.GetCompound("locked")) : new Item();
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add(_stored.IsAir
			? $"Empty  (0 / {MaxAmount:N0})"
			: $"{_stored.Name}: {_storedAmount:N0} / {MaxAmount:N0}");
		if (IsLocked) lines.Add($"Locked: {_lockedItem.Name}");
		if (IsVoiding) lines.Add("Voiding overflow");
		lines.Add("Right-click to open. Deposit through the slot inside the UI");
	}
}
