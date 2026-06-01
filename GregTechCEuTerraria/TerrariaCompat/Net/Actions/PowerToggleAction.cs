#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Set WorkingEnabled to an explicit value. Carries the absolute target rather
// than "toggle" so a duplicated packet (network hiccup, double-click) can't
// silently flip state back - the client's view always converges on the value
// it intended to set.
public sealed class PowerToggleAction : IMachineAction
{
	public PacketType Type => PacketType.PowerToggle;

	private bool _enabled;

	// Parameterless ctor for the HandleIncoming<T> dispatcher.
	public PowerToggleAction() { }
	public PowerToggleAction(bool enabled) { _enabled = enabled; }

	public void Write(BinaryWriter w) => w.Write(_enabled);
	public void Read (BinaryReader r) => _enabled = r.ReadBoolean();

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		entity.WorkingEnabled = _enabled;
	}
}
