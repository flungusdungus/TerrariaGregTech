#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Server-authoritative distinctness setter for an IDistinctPart (item bus).
// Mirrors upstream `IDistinctPart.attachConfigurators`'
// `IFancyConfiguratorButton.Toggle(..., setDistinct(pressed))` - the client
// picks the target state, the server applies + broadcasts.
//
// Absolute target (not toggle) so duplicated packets converge on intent.
// The bus's own SetDistinct clamps `Io != IO.OUT` server-side; a client
// trying to set distinct on an output bus can't sneak it through.
public sealed class DistinctSetAction : IMachineAction
{
	public PacketType Type => PacketType.DistinctSet;

	private bool _distinct;

	public DistinctSetAction() { }
	public DistinctSetAction(bool distinct) { _distinct = distinct; }

	public void Write(BinaryWriter w) => w.Write(_distinct);
	public void Read (BinaryReader r) => _distinct = r.ReadBoolean();

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is IDistinctPart part)
			part.SetDistinct(_distinct);
	}
}
