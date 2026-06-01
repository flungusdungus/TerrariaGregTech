#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike;

// DEVIATION: mode is an explicit per-side field
// on PipeCoverable, NOT derived from cover type. Both view covers always
// exist once initialised; `mode` picks which GetCoverAtSide returns.
//   Off     - no cover takes effect (IsBlocked = true).
//   Passive - filter cover is in effect; flow on neighbour push/pull only.
//   Active  - robot-arm / regulator cover is in effect; pipe initiates flow.
public enum PipeSideMode : byte
{
	Off     = 0,
	Passive = 1,
	Active  = 2,
}
