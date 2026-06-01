#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Creative;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// One packet covers every creative-energy-container knob.
public sealed class CreativeEnergySetAction : IMachineAction
{
	public PacketType Type => PacketType.CreativeEnergySet;

	public enum Op : byte
	{
		Voltage = 1,   // payload: long
		Amps    = 2,   // payload: int
		Active  = 3,   // payload: bool
		Source  = 4,   // payload: bool (true = source, false = sink)
	}

	private Op _op;
	private long _longValue;
	private int _intValue;
	private bool _boolValue;

	public CreativeEnergySetAction() { }
	private CreativeEnergySetAction(Op op) { _op = op; }

	public static CreativeEnergySetAction Voltage(long value) => new(Op.Voltage) { _longValue = value };
	public static CreativeEnergySetAction Amps(int value)     => new(Op.Amps)    { _intValue  = value };
	public static CreativeEnergySetAction Active(bool value)  => new(Op.Active)  { _boolValue = value };
	public static CreativeEnergySetAction Source(bool value)  => new(Op.Source)  { _boolValue = value };

	public void Write(BinaryWriter w)
	{
		w.Write((byte)_op);
		switch (_op)
		{
			case Op.Voltage: w.Write(_longValue); break;
			case Op.Amps:    w.Write(_intValue);  break;
			case Op.Active:
			case Op.Source:  w.Write(_boolValue); break;
		}
	}

	public void Read(BinaryReader r)
	{
		_op = (Op)r.ReadByte();
		switch (_op)
		{
			case Op.Voltage: _longValue = r.ReadInt64(); break;
			case Op.Amps:    _intValue  = r.ReadInt32(); break;
			case Op.Active:
			case Op.Source:  _boolValue = r.ReadBoolean(); break;
		}
	}

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is not CreativeEnergyContainerMachine cec) return;
		switch (_op)
		{
			case Op.Voltage: cec.Voltage = _longValue; break;
			case Op.Amps:    cec.Amps    = _intValue;  break;
			case Op.Active:  cec.Active  = _boolValue; break;
			case Op.Source:  cec.Source  = _boolValue; break;
		}
	}
}
