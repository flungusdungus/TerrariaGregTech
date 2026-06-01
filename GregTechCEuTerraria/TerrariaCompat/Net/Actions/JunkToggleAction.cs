#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Set the fisher's `junkEnabled` flag to an explicit value. Absolute target
// (not delta) so a duplicated packet - double-click, slow network - converges
// on what the client intended. Mirrors upstream FisherMachine.setJunkEnabled
// (the `gtceu.gui.fisher_mode` toggle button).
public sealed class JunkToggleAction : IMachineAction
{
	public PacketType Type => PacketType.JunkToggle;

	private bool _enabled;

	public JunkToggleAction() { }
	public JunkToggleAction(bool enabled) { _enabled = enabled; }

	public void Write(BinaryWriter w) => w.Write(_enabled);
	public void Read (BinaryReader r) => _enabled = r.ReadBoolean();

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is FisherMachine fisher) fisher.JunkEnabled = _enabled;
	}
}
