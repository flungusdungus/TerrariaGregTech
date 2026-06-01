#nullable enable
using GregTechCEuTerraria.Api.Transfer;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Port of virtualregistry.entries.VirtualItemStorage - 1-slot shared buffer.
public sealed class VirtualItemStorage : VirtualEntry
{
	private const int DefaultSlotAmount = 1;

	public CustomItemStackHandler Handler { get; } = new(DefaultSlotAmount);

	public override EnderEntryType Type => EnderEntryType.Item;

	public bool IsEmpty()
	{
		for (int i = 0; i < Handler.SlotCount; i++)
			if (!Handler.GetSlot(i).IsAir) return false;
		return true;
	}

	public override bool CanRemove() => base.CanRemove() && IsEmpty();

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["items"] = Handler.SerializeNBT();
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("items")) Handler.DeserializeNBT(tag.GetCompound("items"));
	}
}
