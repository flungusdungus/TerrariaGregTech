#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Server-authoritative parallel-count setter for a ParallelHatchPartMachine.
// Mirrors the role upstream's `IntInputWidget` plays - the client picks a
// target value, the server clamps + applies + broadcasts.
//
// Absolute target (not delta) so duplicated packets converge on intent.
// Clamping (MIN_PARALLEL..MaxParallel) runs server-side inside
// SetCurrentParallel; a malformed client value can't push out of range.
public sealed class ParallelSetAction : IMachineAction
{
	public PacketType Type => PacketType.ParallelSet;

	private int _value;

	public ParallelSetAction() { }
	public ParallelSetAction(int value) { _value = value; }

	public void Write(BinaryWriter w) => w.Write(_value);
	public void Read (BinaryReader r) => _value = r.ReadInt32();

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is ParallelHatchPartMachine hatch)
			hatch.SetCurrentParallel(_value);
	}
}
