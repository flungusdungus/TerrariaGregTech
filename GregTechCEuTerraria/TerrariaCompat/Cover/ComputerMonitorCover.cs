#nullable enable
using GregTechCEuTerraria.Api.Cover;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Recipe-only placeholder for `computer_monitor`. Not implemented, not planned
// - upstream depends on Create-mod display-link / ComputerCraft (no Terraria
// analogue). CanAttach=false.
public sealed class ComputerMonitorCover : CoverBehavior
{
	public ComputerMonitorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override bool CanAttach() => false;
}
