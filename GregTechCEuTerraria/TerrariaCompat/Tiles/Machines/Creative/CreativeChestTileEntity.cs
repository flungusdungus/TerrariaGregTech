#nullable enable
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Creative;

// Port of com.gregtechceu.gtceu.common.machine.storage.CreativeChestMachine.
// Infinite item source for debug: configurable source type (_stored), items per
// AutoOutput cycle (ItemsPerCycle), cycle period (TicksPerCycle). Insert matching
// source -> silently accepted, mismatch -> rejected; Extract always returns
// ItemsPerCycle of the source (never depletes); AutoOutput fires on TicksPerCycle.
//
// DEVIATION: InfiniteCache collapsed onto the machine (override
// Insert/Extract/GetSlot/IsItemValid), same shape as the SuperChest port.
public sealed class CreativeChestTileEntity : SuperChestTileEntity
{
	public CreativeChestTileEntity() { }

	protected override string Label => Definition?.Label ?? "Creative Chest";

	private int _itemsPerCycle = 1;
	private int _ticksPerCycle = 1;

	public int ItemsPerCycle
	{
		get => _itemsPerCycle;
		set => _itemsPerCycle = Math.Max(1, value);
	}

	public int TicksPerCycle
	{
		get => _ticksPerCycle;
		set
		{
			_ticksPerCycle = Math.Max(1, value);
			// upstream autoOutput.setTicksPerCycle
			if (AutoOutput is not null) AutoOutput.TicksPerCycle = _ticksPerCycle;
		}
	}

	// upstream updateStored(item): copy the held item (count 1) into _stored
	// (server-authoritative via the UI phantom slot). Empty clears the source.
	public void SetSourceType(Item item)
	{
		if (item is null || item.IsAir)
		{
			_stored = new Item();
			_storedAmount = 0;
		}
		else
		{
			_stored = item.Clone();
			_stored.stack = 1;
			_storedAmount = 1;   // marker - keeps GetSlot displaying the item
		}
	}

	// upstream InfiniteCache.{getStackInSlot, insertItem, extractItem, isItemValid}.
	public override Item GetSlot(int slot)
	{
		if (_stored.IsAir) return new Item();
		var view = _stored.Clone();
		view.stack = 1;
		return view;
	}

	public override Item Insert(int slot, Item item, bool simulate)
	{
		// Matching source -> accepted (EMPTY), else rejected (returned).
		if (item is null || item.IsAir) return new Item();
		if (!_stored.IsAir && _stored.type == item.type) return new Item();
		return item.Clone();
	}

	public override Item Extract(int slot, int amount, bool simulate)
	{
		// Always ItemsPerCycle of the source (ignores `amount`, per upstream).
		if (_stored.IsAir) return new Item();
		var copy = _stored.Clone();
		copy.stack = _itemsPerCycle;
		return copy;
	}

	public override bool IsItemValid(int slot, Item item) => true;

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["itemsPerCycle"] = _itemsPerCycle;
		tag["ticksPerCycle"] = _ticksPerCycle;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		_itemsPerCycle = tag.ContainsKey("itemsPerCycle") ? Math.Max(1, tag.GetInt("itemsPerCycle")) : 1;
		_ticksPerCycle = tag.ContainsKey("ticksPerCycle") ? Math.Max(1, tag.GetInt("ticksPerCycle")) : 1;
		if (AutoOutput is not null) AutoOutput.TicksPerCycle = _ticksPerCycle;
	}

	// Carry the source item across break -> re-place.
	public override void WritePortableData(TagCompound tag)
	{
		if (!_stored.IsAir) tag["stored"] = ItemIO.Save(_stored);
		tag["itemsPerCycle"] = _itemsPerCycle;
		tag["ticksPerCycle"] = _ticksPerCycle;
	}

	public override void ReadPortableData(TagCompound tag)
	{
		if (tag.ContainsKey("stored"))
		{
			_stored = ItemIO.Load(tag.GetCompound("stored"));
			_storedAmount = 1;
		}
		if (tag.ContainsKey("itemsPerCycle")) _itemsPerCycle = Math.Max(1, tag.GetInt("itemsPerCycle"));
		if (tag.ContainsKey("ticksPerCycle")) _ticksPerCycle = Math.Max(1, tag.GetInt("ticksPerCycle"));
		if (AutoOutput is not null) AutoOutput.TicksPerCycle = _ticksPerCycle;
	}

	public override void AppendTooltip(List<string> lines)
	{
		// Skip SuperChest's stored/lock/void lines (N/A to an infinite source);
		// just the header + creative-source info.
		lines.Add(DisplayName);
		lines.Add(_stored.IsAir ? "Source: (unset)" : $"Source: {_stored.Name}");
		lines.Add($"Rate: {_itemsPerCycle} items / {_ticksPerCycle}t");
		if (!IsAutoOutput) lines.Add("Auto-output: disabled");
	}
}
