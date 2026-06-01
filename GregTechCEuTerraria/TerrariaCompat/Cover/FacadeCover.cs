#nullable enable
using GregTechCEuTerraria.Api.Cover;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.FacadeCover. Pure-rendering upstream; collapses to
// a no-op in 2D. canPipePassThrough=false is kept (facades block pipes).
public sealed class FacadeCover : CoverBehavior
{
	public FacadeCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override bool CanPipePassThrough() => false;
}
