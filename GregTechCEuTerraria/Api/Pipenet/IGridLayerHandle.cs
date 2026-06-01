#nullable enable
using Terraria;

namespace GregTechCEuTerraria.Api.Pipenet;

// Universal accessor for "place / cut / refund / broadcast on one layer".
// Implemented by CableLayerHandle, ItemPipeLayerHandle, FluidPipeLayerHandle
// (and any future grid-style layer). The narrow surface here is the
// cross-cutting one - querying the layer + removing-and-refunding a cell -
// because it's the only piece of behaviour a layer-agnostic actor (a wire
// cutter, a multi-layer debug tool, ...) needs to invoke uniformly.
//
// Layer-specific placement signatures live on the concrete handles
// (CableLayerHandle.TryPlace, ItemPipeLayerHandle.TryPlace, ...) - they
// can't go on the universal interface because each layer's cell carries a
// different field set (cable has voltage / amperage / loss; fluid pipe has
// throughput / channels / temperature / containment-proofs; item pipe has
// the restrictive flag). Callers that know their layer reach for the
// concrete handle's typed TryPlace; the Has + CutAt pair here is fully
// sufficient for any layer-agnostic consumer (a wire cutter, a multi-layer
// debug tool, ...).
public interface IGridLayerHandle
{
	// Is there a cell at (x, y) on the layer this handle owns?
	bool Has(int x, int y);

	// Remove the cell at (x, y) if present, refund the matching item to the
	// `remover`, ship the layer's removal packet. Returns true iff a cell
	// was actually removed. Never touches any layer but its own.
	bool CutAt(int x, int y, Player remover);
}
