#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Per-item NBT blob for machine-placer items. Universal - attaches to every
// item that places one of our machine tiles. Set by MetaMachineTile.GetItemDrops
// (via MetaMachine.WritePortableData override), consumed by placement.
// Machines that don't override keep Data null = plain placeable (upstream
// parity: machines don't keep state when broken).
public sealed class MachinePortableData : GlobalItem
{
	public override bool InstancePerEntity => true;

	public TagCompound? Data;

	// IMachineTextureSpec is implemented exclusively by MetaMachineTile<T>,
	// so it doubles as the "one of our machine tiles" marker.
	public override bool AppliesToEntity(Item item, bool lateInstantiation) =>
		item.createTile >= TileID.Count && TileLoader.GetTile(item.createTile) is IMachineTextureSpec;

	public override void SaveData(Item item, TagCompound tag)
	{
		if (Data is { Count: > 0 }) tag["portable"] = Data;
	}

	public override void LoadData(Item item, TagCompound tag)
	{
		Data = tag.ContainsKey("portable") ? tag.GetCompound("portable") : null;
	}

	// ItemIO.Send writes just netID/stack - need this for MP wire sync.
	public override void NetSend(Item item, BinaryWriter writer)
	{
		bool has = Data is { Count: > 0 };
		writer.Write(has);
		if (has) TagIO.Write(Data!, writer);
	}

	public override void NetReceive(Item item, BinaryReader reader)
	{
		Data = reader.ReadBoolean() ? TagIO.Read(reader) : null;
	}

	// Stacking would silently drop one blob (= dupe/void). Matches
	// FluidCellItem's filled-cell gating.
	public override bool CanStack(Item destination, Item source) => !EitherHasData(destination, source);

	public override bool CanStackInWorld(Item destination, Item source) => !EitherHasData(destination, source);

	private static bool EitherHasData(Item a, Item b)
	{
		var ad = a.GetGlobalItem<MachinePortableData>();
		var bd = b.GetGlobalItem<MachinePortableData>();
		return ad.Data is { Count: > 0 } || bd.Data is { Count: > 0 };
	}

	// Base MemberwiseClone would share the TagCompound - deep-copy.
	public override GlobalItem Clone(Item? from, Item to)
	{
		var clone = (MachinePortableData)base.Clone(from, to);
		clone.Data = Data is null ? null : (TagCompound)Data.Clone();
		return clone;
	}
}
