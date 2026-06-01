#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Set a multiblock part's `IoDirection` to an explicit value. Used by the
// abstract UI's part-direction cluster - the same way `PowerToggleAction`
// carries the absolute target rather than "toggle", so a duplicated packet
// can't silently flip state.
//
// Only `TieredIOPartMachine` carries IoDirection; the action no-ops on any
// other entity (defensive - packet dispatch can be triggered from a stale
// client view if the GUI's target changes mid-flight).
public sealed class PartIoDirectionSetAction : IMachineAction
{
	public PacketType Type => PacketType.PartIoDirection;

	private IODirection _direction;

	public PartIoDirectionSetAction() { }
	public PartIoDirectionSetAction(IODirection direction) { _direction = direction; }

	public void Write(BinaryWriter w) => w.Write((byte)_direction);
	public void Read (BinaryReader r) => _direction = (IODirection)r.ReadByte();

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is TieredIOPartMachine part)
			part.SetIoDirection(_direction);
	}
}
