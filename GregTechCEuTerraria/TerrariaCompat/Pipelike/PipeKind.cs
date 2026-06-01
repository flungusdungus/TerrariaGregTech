#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike;

// Layer discriminator. Restrictive item pipes live on Item with a per-cell
// Restrictive flag, NOT a separate layer.
public enum PipeKind : byte
{
	Item  = 0,
	Fluid = 1,
	// Single NORMAL variant from upstream. Connects only along one axis;
	// LaserNetWalker enforces. No per-side covers (the existing
	// PipeCoverable / PipeNeighborWatcher path is item/fluid-only).
	Laser = 2,
	// Single NORMAL variant. Carries research data + computation (CWU) along
	// one axis; OpticalNetWalker enforces. Like Laser - no per-side covers,
	// zero cell payload.
	Optical = 3,
	// Unified long-distance item + fluid layer. One cell carries a Type byte
	// (item / fluid); the two types never merge (Node.Mark separation). No
	// per-side covers. Wormhole transfer lives on the endpoint machines, not
	// the net. The place/remove/sync packets carry the type byte in the payload.
	LongDistance = 4,
}
