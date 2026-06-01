#nullable enable
using GregTechCEuTerraria.Api.Cover;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Recipe-only placeholder for `wireless_transmitter`. Not implemented, not
// planned - Create + ComputerCraft bridge (no Terraria analogue). CanAttach=false.
public sealed class WirelessTransmitterCover : CoverBehavior
{
	public WirelessTransmitterCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override bool CanAttach() => false;
}
