#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Creative;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Single packet covers every creative chest config knob. One of three Op
// variants per send keeps the wire payload tight and the apply logic local
// to each field.
public sealed class CreativeChestSetAction : IMachineAction
{
	public PacketType Type => PacketType.CreativeChestSet;

	public enum Op : byte
	{
		SetSourceType  = 1,   // payload: Item NBT (or empty tag to clear)
		ItemsPerCycle  = 2,   // payload: int
		TicksPerCycle  = 3,   // payload: int
	}

	private Op _op;
	private Item _item = new();
	private int _intValue;

	public CreativeChestSetAction() { }

	private CreativeChestSetAction(Op op) { _op = op; }
	public static CreativeChestSetAction SetSourceType(Item? item)
		=> new(Op.SetSourceType) { _item = item is null ? new Item() : item.Clone() };
	public static CreativeChestSetAction ItemsPerCycle(int value)
		=> new(Op.ItemsPerCycle) { _intValue = value };
	public static CreativeChestSetAction TicksPerCycle(int value)
		=> new(Op.TicksPerCycle) { _intValue = value };

	public void Write(BinaryWriter w)
	{
		w.Write((byte)_op);
		switch (_op)
		{
			case Op.SetSourceType:
				if (_item is null || _item.IsAir) { w.Write(false); }
				else { w.Write(true); TagIO.Write(ItemIO.Save(_item), w); }
				break;
			case Op.ItemsPerCycle:
			case Op.TicksPerCycle:
				w.Write(_intValue);
				break;
		}
	}

	public void Read(BinaryReader r)
	{
		_op = (Op)r.ReadByte();
		switch (_op)
		{
			case Op.SetSourceType:
				_item = r.ReadBoolean() ? ItemIO.Load(TagIO.Read(r)) : new Item();
				break;
			case Op.ItemsPerCycle:
			case Op.TicksPerCycle:
				_intValue = r.ReadInt32();
				break;
		}
	}

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is not CreativeChestTileEntity chest) return;
		switch (_op)
		{
			case Op.SetSourceType: chest.SetSourceType(_item); break;
			case Op.ItemsPerCycle: chest.ItemsPerCycle = _intValue; break;
			case Op.TicksPerCycle: chest.TicksPerCycle = _intValue; break;
		}
	}
}
