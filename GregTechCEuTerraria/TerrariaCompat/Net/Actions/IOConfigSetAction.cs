#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Set a single field of a machine's AutoOutputTrait to an absolute value.
//
// Per-field rather than whole-snapshot so concurrent viewers can mutate
// orthogonal fields without one client clobbering another with a stale
// snapshot (player A flips ItemOutputSide while player B flips
// AllowFluidInputFromOutput - both should land, not race).
//
// The payload is `(field byte, value byte)`. Value semantics depend on field:
//   - Direction fields -> IODirection cast.
//   - Bool fields      -> 0/1.
// Server clamps/ignores out-of-range values.
public sealed class IOConfigSetAction : IMachineAction
{
	public enum Field : byte
	{
		ItemOutputSide          = 0,
		FluidOutputSide         = 1,
		ItemAutoOutput          = 2,
		FluidAutoOutput         = 3,
		AllowItemInputFromOutput  = 4,
		AllowFluidInputFromOutput = 5,
	}

	public PacketType Type => PacketType.IOConfigSet;

	private Field _field;
	private byte _value;

	public IOConfigSetAction() { }
	public IOConfigSetAction(Field field, byte value) { _field = field; _value = value; }

	// Convenience ctors for the two value kinds.
	public static IOConfigSetAction OfDirection(Field field, IODirection dir) => new(field, (byte)dir);
	public static IOConfigSetAction OfBool(Field field, bool v) => new(field, v ? (byte)1 : (byte)0);

	public void Write(BinaryWriter w) { w.Write((byte)_field); w.Write(_value); }
	public void Read (BinaryReader r) { _field = (Field)r.ReadByte(); _value = r.ReadByte(); }

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		// Machines with no auto-output config (steam boilers, transformers,
		// generators) have no AutoOutputTrait - a stray packet is a no-op.
		var ao = entity.AutoOutput;
		if (ao is null) return;
		bool b = _value != 0;
		// Out-of-range direction -> silently coerced to None (defensive against
		// a hacked client; UI widget can't produce this).
		IODirection dir = _value <= (byte)IODirection.Right ? (IODirection)_value : IODirection.None;
		switch (_field)
		{
			case Field.ItemOutputSide:             ao.SetItemOutputDirection(dir); break;
			case Field.FluidOutputSide:            ao.SetFluidOutputDirection(dir); break;
			case Field.ItemAutoOutput:             ao.SetAllowAutoOutputItems(b); break;
			case Field.FluidAutoOutput:            ao.SetAllowAutoOutputFluids(b); break;
			case Field.AllowItemInputFromOutput:   ao.SetAllowItemInputFromOutputSide(b); break;
			case Field.AllowFluidInputFromOutput:  ao.SetAllowFluidInputFromOutputSide(b); break;
		}
	}
}
