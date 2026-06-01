#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Capability;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Shared (de)serialization primitives. Change here = every packet stays
// compatible.
public static class NetHelpers
{
	public static void WritePoint16(this BinaryWriter w, Point16 p)
	{
		w.Write((short)p.X);
		w.Write((short)p.Y);
	}
	public static Point16 ReadPoint16(this BinaryReader r) => new(r.ReadInt16(), r.ReadInt16());

	// Tag form preserves prefix / modded data / shimmered.
	public static void WriteItem(this BinaryWriter w, Item item)
	{
		if (item is null || item.IsAir) { w.Write((byte)0); return; }
		w.Write((byte)1);
		var tag = ItemIO.Save(item);
		TagIO.Write(tag, w);
	}
	public static Item ReadItem(this BinaryReader r)
	{
		byte kind = r.ReadByte();
		if (kind == 0) return new Item();
		var tag = TagIO.Read(r);
		return ItemIO.Load(tag);
	}

	public static void WriteFluidStack(this BinaryWriter w, FluidStack stack)
	{
		if (stack.IsEmpty)
		{
			w.Write((byte)0);
			return;
		}
		w.Write((byte)1);
		w.Write(stack.Type!.Id);
		w.Write(stack.Amount);
	}
	public static FluidStack ReadFluidStack(this BinaryReader r)
	{
		byte kind = r.ReadByte();
		if (kind == 0) return FluidStack.Empty;
		string id = r.ReadString();
		int amount = r.ReadInt32();
		if (FluidRegistry.TryGet(id, out var type)) return new FluidStack(type, amount);
		return FluidStack.Empty; // unknown fluid (mod unloaded)
	}

	public static void LogBadPacket(string context, string detail)
	{
		var mod = ModLoader.GetMod("GregTechCEuTerraria");
		mod.Logger.Warn($"[net:{context}] {detail}");
	}
}
