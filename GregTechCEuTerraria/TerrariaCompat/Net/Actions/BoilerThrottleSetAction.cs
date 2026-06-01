#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Steam;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Set a Large Boiler's throttle to an absolute value (25-100). Mirrors
// upstream LargeBoilerMachine.handleDisplayClick's `+/-5` step - the UI sends
// the new absolute value rather than a delta so a duplicated packet can't
// silently drift the throttle (same idiom as PowerToggleAction).
public sealed class BoilerThrottleSetAction : IMachineAction
{
	public PacketType Type => PacketType.BoilerThrottleSet;

	private byte _throttle;

	public BoilerThrottleSetAction() { }
	public BoilerThrottleSetAction(int throttle) { _throttle = (byte)System.Math.Clamp(throttle, 25, 100); }

	public void Write(BinaryWriter w) => w.Write(_throttle);
	public void Read (BinaryReader r) => _throttle = r.ReadByte();

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is LargeBoilerMachine boiler)
			boiler.SetThrottle(_throttle);
	}
}
