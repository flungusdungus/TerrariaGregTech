#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Transfer;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.StorageCover - 18-slot drawer attached to a machine
// side, no capability exposure, contents drop on removal. createUIWidget +
// LDLib widgets dropped.
public class StorageCover : CoverBehavior, IUICover
{
	private const int Size = 18;

	public CustomItemStackHandler Inventory { get; } = new StorageInventory(Size);

	public StorageCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	// Verbatim: one StorageCover per machine. (Upstream's
	// MachineCoverContainer host-type check collapses - every ICoverable here
	// is a machine.)
	public override bool CanAttach()
	{
		if (!base.CanAttach()) return false;
		foreach (var side in CoverSides.All)
			if (CoverHolder.GetCoverAtSide(side) is StorageCover)
				return false;
		return true;
	}

	public override List<Item> GetAdditionalDrops()
	{
		var list = base.GetAdditionalDrops();
		for (int slot = 0; slot < Size; slot++)
		{
			var stack = Inventory.GetSlot(slot);
			if (!stack.IsAir) list.Add(stack);
		}
		return list;
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["inventory"] = Inventory.SerializeNBT();
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("inventory")) Inventory.DeserializeNBT(tag.GetCompound("inventory"));
	}

	// Verbatim upstream anonymous getSlotLimit -> 1.
	private sealed class StorageInventory : CustomItemStackHandler
	{
		public StorageInventory(int size) : base(size) { }
		public override int GetSlotLimit(int slot) => 1;
	}
}
