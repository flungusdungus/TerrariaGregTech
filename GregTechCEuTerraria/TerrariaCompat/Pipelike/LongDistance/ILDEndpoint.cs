#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// Port of com.gregtechceu.gtceu.api.pipenet.longdistance.ILDEndpoint.
//
// An endpoint is a machine that caps a long-distance pipe network. Its IO role
// (IN / OUT) is player-set via a screwdriver flip (upstream derives it from
// front-vs-back facing; we have no facing - see "Endpoint IO model" deviation).
//
// 2D adaptation: upstream's getFrontFacing()/getOutputFacing() pair collapses to
// PipeSide (the single cardinal side touching the LD network, auto-detected) and
// TargetSide (its opposite, where the source / destination inventory sits).
public interface ILDEndpoint
{
	LongDistancePipeType PipeType { get; }

	// Player-set IO role. IO.IN = network input (the source inventory / pipe on
	// TargetSide pushes in, items travel across the net). IO.OUT = network output
	// (the link's wormhole writes into the inventory on this endpoint's
	// TargetSide). IO.NONE = unconfigured / invalid (no single pipe side).
	IO IoType { get; set; }

	bool IsInput  => IoType == IO.IN;
	bool IsOutput => IoType == IO.OUT;

	// Tile position of this endpoint (top-left anchor of the 2-tile cell).
	(int x, int y) EndpointPos { get; }

	// The single cardinal side adjacent to an LD pipe cell of PipeType, or
	// IODirection.None when 0 or >1 such sides exist (invalid configuration).
	IODirection PipeSide { get; }

	// Opposite of PipeSide - the inventory-facing side.
	IODirection TargetSide => PipeSide.Opposite();

	// The LD pipe net this endpoint is attached to (resolved from the adjacent
	// pipe cell on PipeSide), or null when unattached. Used by the registry to
	// pair endpoints sharing one connected component.
	LongDistancePipeNet? AttachedNet { get; }

	// The currently linked partner endpoint (resolved via the shared net), or
	// null when this endpoint has no valid wormhole partner.
	ILDEndpoint? GetLink();

	bool IsRemoved { get; }

	void InvalidateLink();
}
