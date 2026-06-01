#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// Verbatim port of com.gregtechceu.gtceu.common.pipelike.laser.LaserRoutePath.
//
// Identifies the unique ILaserContainer endpoint a per-side LaserNetHandler
// reaches. `TargetFacing` is the side of `TargetPipePos` that faces the
// endpoint tile.
//
// === Documented adaptations =================================================
//
//   - `BlockPos` -> `(int x, int y)`.
//   - `Direction` -> `IODirection` (cardinal only).
//   - `getHandler(Level)` -> `GetHandler()` - we have one world (Terraria's
//     Main), no per-level state.
//   - `IAttachData` (paint + connections bitfield) DROPPED - paint isn't
//     ported; connections are tracked on the cell, not the route path.
public sealed class LaserRoutePath
{
	public (int x, int y) TargetPipePos { get; }
	public IODirection    TargetFacing  { get; }
	public int            Distance      { get; }

	public LaserRoutePath((int x, int y) targetPipePos, IODirection targetFacing, int distance)
	{
		TargetPipePos = targetPipePos;
		TargetFacing  = targetFacing;
		Distance      = distance;
	}

	// Returns the ILaserContainer at the cell adjacent to the target pipe on
	// the `TargetFacing` side, if one is present. Mirrors upstream's
	// `GTCapabilityHelper.getLaser(level, pipePos.relative(facing), facing.
	// opposite())`.
	public ILaserContainer? GetHandler()
	{
		var (dx, dy) = TargetFacing.Offset();
		int hx = TargetPipePos.x + dx;
		int hy = TargetPipePos.y + dy;
		return WorldCapability.Get<ILaserContainer>(hx, hy);
	}
}
