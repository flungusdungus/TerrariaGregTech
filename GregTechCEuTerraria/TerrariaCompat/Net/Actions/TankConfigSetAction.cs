#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Set a single fluid-machine toggle (Locked, Voiding, AutoOutput).
//
// Locked / Voiding are Super-Tank-only; AutoOutput is generic - routed through
// IControllable.SetWorkingEnabled, which both the Super Tank and the Drum alias
// to their fluid auto-output toggle. Locked has a side effect (snapping the
// locked type to what's stored) so it routes through SuperTankTileEntity.
public sealed class TankConfigSetAction : IMachineAction
{
	public enum Field : byte
	{
		Locked     = 0,
		Voiding    = 1,
		AutoOutput = 2,
	}

	public PacketType Type => PacketType.TankConfigSet;

	private Field _field;
	private bool _value;

	public TankConfigSetAction() { }
	public TankConfigSetAction(Field field, bool value) { _field = field; _value = value; }

	public void Write(BinaryWriter w) { w.Write((byte)_field); w.Write(_value); }
	public void Read (BinaryReader r) { _field = (Field)r.ReadByte(); _value = r.ReadBoolean(); }

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		switch (_field)
		{
			case Field.Locked  when entity is SuperTankTileEntity t:  t.SetLocked(_value); break;
			case Field.Voiding when entity is SuperTankTileEntity t2: t2.IsVoiding = _value; break;
			// AutoOutput is generic - Super Tank + Drum both alias
			// IControllable working-enabled to their fluid auto-output toggle.
			case Field.AutoOutput when entity is Api.Capability.IControllable c:
				c.SetWorkingEnabled(_value); break;
		}
	}
}
