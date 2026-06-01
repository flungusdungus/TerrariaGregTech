#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Creative;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Companion to CreativeChestSetAction - same Op shape, FluidType-keyed.
public sealed class CreativeTankSetAction : IMachineAction
{
	public PacketType Type => PacketType.CreativeTankSet;

	public enum Op : byte
	{
		SetSourceFluid = 1,   // payload: fluid id string (empty = clear)
		MBPerCycle     = 2,   // payload: int
		TicksPerCycle  = 3,   // payload: int
	}

	private Op _op;
	private string _fluidId = "";
	private int _intValue;

	public CreativeTankSetAction() { }
	private CreativeTankSetAction(Op op) { _op = op; }

	public static CreativeTankSetAction SetSourceFluid(FluidType? type)
		=> new(Op.SetSourceFluid) { _fluidId = type?.Id ?? "" };
	public static CreativeTankSetAction MBPerCycle(int value)
		=> new(Op.MBPerCycle) { _intValue = value };
	public static CreativeTankSetAction TicksPerCycle(int value)
		=> new(Op.TicksPerCycle) { _intValue = value };

	public void Write(BinaryWriter w)
	{
		w.Write((byte)_op);
		switch (_op)
		{
			case Op.SetSourceFluid: w.Write(_fluidId ?? ""); break;
			case Op.MBPerCycle:
			case Op.TicksPerCycle: w.Write(_intValue); break;
		}
	}

	public void Read(BinaryReader r)
	{
		_op = (Op)r.ReadByte();
		switch (_op)
		{
			case Op.SetSourceFluid: _fluidId = r.ReadString(); break;
			case Op.MBPerCycle:
			case Op.TicksPerCycle: _intValue = r.ReadInt32(); break;
		}
	}

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is not CreativeTankTileEntity tank) return;
		switch (_op)
		{
			case Op.SetSourceFluid:
				if (string.IsNullOrEmpty(_fluidId))      tank.SetSourceFluid(null);
				else if (FluidRegistry.TryGet(_fluidId, out var t)) tank.SetSourceFluid(t);
				break;
			case Op.MBPerCycle:    tank.MBPerCycle    = _intValue; break;
			case Op.TicksPerCycle: tank.TicksPerCycle = _intValue; break;
		}
	}
}
