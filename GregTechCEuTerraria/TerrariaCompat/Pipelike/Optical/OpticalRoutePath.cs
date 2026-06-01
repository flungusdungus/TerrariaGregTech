#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// Verbatim port of com.gregtechceu.gtceu.common.pipelike.optical.OpticalRoutePath.
//
// Identifies the unique endpoint a per-side OpticalNetHandler reaches. Unlike
// the laser route (one capability), an optical route resolves TWO endpoint
// capabilities at the same target cell: the research-data hatch
// (`IOpticalDataAccessHatch`) and the computation provider
// (`IOpticalComputationProvider`). `TargetFacing` is the side of
// `TargetPipePos` that faces the endpoint tile.
//
// === Documented adaptations =================================================
//
//   - `BlockPos` -> `(int x, int y)`; `Direction` -> `IODirection`.
//   - `getTargetCapability(level)` -> `WorldCapability.Get<T>(hx, hy)` - we have
//     one world (Terraria's Main), no per-level state.
//   - `getDataHatch()` returns the endpoint cast to `IOpticalDataAccessHatch`
//     (verbatim: upstream casts the `CAPABILITY_DATA_ACCESS` result to the
//     optical sub-interface, null otherwise).
public sealed class OpticalRoutePath
{
	public (int x, int y) TargetPipePos { get; }
	public IODirection    TargetFacing  { get; }
	public int            Distance      { get; }

	public OpticalRoutePath((int x, int y) targetPipePos, IODirection targetFacing, int distance)
	{
		TargetPipePos = targetPipePos;
		TargetFacing  = targetFacing;
		Distance      = distance;
	}

	private (int hx, int hy) EndpointCell()
	{
		var (dx, dy) = TargetFacing.Offset();
		return (TargetPipePos.x + dx, TargetPipePos.y + dy);
	}

	// Mirrors upstream getDataHatch(): resolve the DATA_ACCESS capability at the
	// endpoint and return it only if it is an optical (transmit/receive) hatch.
	public IOpticalDataAccessHatch? GetDataHatch()
	{
		var (hx, hy) = EndpointCell();
		return WorldCapability.Get<IDataAccessHatch>(hx, hy) as IOpticalDataAccessHatch;
	}

	// Mirrors upstream getComputationHatch(): resolve the COMPUTATION_PROVIDER
	// capability at the endpoint.
	public IOpticalComputationProvider? GetComputationHatch()
	{
		var (hx, hy) = EndpointCell();
		return WorldCapability.Get<IOpticalComputationProvider>(hx, hy);
	}
}
