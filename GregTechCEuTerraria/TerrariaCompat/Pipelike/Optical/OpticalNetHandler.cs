#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// Verbatim port of com.gregtechceu.gtceu.common.pipelike.optical.OpticalNetHandler.
//
// Per-side capability the optical pipe exposes outward - it is BOTH an
// `IDataAccessHatch` (research-data lookup) and an `IOpticalComputationProvider`
// (CWU/t). When an adjacent endpoint queries one of these through the pipe's
// face, the handler resolves the unique destination via the net's route cache
// and delegates. Mirror of LaserNetHandler.
//
// === Documented adaptations =================================================
//
//   - `OpticalPipeBlockEntity pipe` -> `(int x, int y) pipePos` + axis-side
//     `_facing` field. The layer's cell is the source of truth.
//   - `pipe.isRemoved()` check -> layer membership check.
//   - `setActive(true, 100)` on the route -> per-cell tick counter in
//     `OpticalPipeLayerSystem.SetActive(pos, ticks)`.
public sealed class OpticalNetHandler : IDataAccessHatch, IOpticalComputationProvider
{
	private OpticalPipeNet _net;
	private readonly (int x, int y) _pipePos;
	private readonly IODirection    _facing;

	public OpticalNetHandler(OpticalPipeNet net, (int x, int y) pipePos, IODirection facing)
	{
		_net     = net;
		_pipePos = pipePos;
		_facing  = facing;
	}

	public void UpdateNetwork(OpticalPipeNet net) => _net = net;
	public OpticalPipeNet GetNet() => _net;

	private void SetPipesActive()
	{
		foreach (var pos in _net.AllNodes.Keys)
			OpticalPipeLayerSystem.SetActive(pos.x, pos.y, ticks: 100);
	}

	private bool IsNetInvalidForTraversal() =>
		_net == null || _facing == IODirection.None || !OpticalPipeLayerSystem.Pipes.Has(_pipePos.x, _pipePos.y);

	// === IDataAccessHatch ===================================================

	public bool IsRecipeAvailable(GTRecipe recipe, ICollection<IDataAccessHatch> seen)
	{
		bool available = TraverseRecipeAvailable(recipe, seen);
		if (available) SetPipesActive();
		return available;
	}

	public bool IsCreative() => false;

	public GTRecipe? ModifyRecipe(GTRecipe recipe)
	{
		if (IsCreative()) return recipe;
		if (IsRecipeAvailable(recipe, NewDataSeen())) return recipe;
		return null;
	}

	private bool TraverseRecipeAvailable(GTRecipe recipe, ICollection<IDataAccessHatch> seen)
	{
		if (IsNetInvalidForTraversal()) return false;
		var inv = _net.GetNetData(_pipePos, _facing);
		if (inv == null) return false;
		var hatch = inv.GetDataHatch();
		if (hatch == null || seen.Contains(hatch)) return false;
		if (hatch.IsTransmitter())
			return hatch.IsRecipeAvailable(recipe, seen);
		return false;
	}

	// Diagnostic: the remote optical data hatch this handler resolves to across
	// the net (or null if the net/route doesn't reach one). Used by the
	// hover-tooltip chain tracer.
	public IOpticalDataAccessHatch? ResolveRemoteDataHatch()
	{
		if (IsNetInvalidForTraversal()) return null;
		return _net.GetNetData(_pipePos, _facing)?.GetDataHatch();
	}

	// Diagnostic count (see IDataAccessHatch.CountVisibleResearch): resolve the
	// remote endpoint across the net and forward to the transmitter on the far
	// side. Mirrors TraverseRecipeAvailable's resolution exactly.
	public int CountVisibleResearch(ICollection<IDataAccessHatch> seen)
	{
		if (IsNetInvalidForTraversal()) return 0;
		var inv = _net.GetNetData(_pipePos, _facing);
		var hatch = inv?.GetDataHatch();
		if (hatch == null || seen.Contains(hatch)) return 0;
		return hatch.IsTransmitter() ? hatch.CountVisibleResearch(seen) : 0;
	}

	// === IOpticalComputationProvider ========================================

	public int RequestCWUt(int cwut, bool simulate, ICollection<IOpticalComputationProvider> seen)
	{
		if (cwut == 0) return 0;
		var provider = GetComputationProvider(seen);
		if (provider == null) return 0;
		int provided = provider.RequestCWUt(cwut, simulate, seen);
		if (provided > 0) SetPipesActive();
		return provided;
	}

	public int RequestCWUt(int cwut, bool simulate) => RequestCWUt(cwut, simulate, NewCompSeen());

	public int GetMaxCWUt(ICollection<IOpticalComputationProvider> seen)
	{
		var provider = GetComputationProvider(seen);
		return provider?.GetMaxCWUt(seen) ?? 0;
	}

	public int GetMaxCWUt() => GetMaxCWUt(NewCompSeen());

	public bool CanBridge(ICollection<IOpticalComputationProvider> seen)
	{
		var provider = GetComputationProvider(seen);
		// nothing found, so don't report a problem, just pass quietly
		return provider == null || provider.CanBridge(seen);
	}

	public bool CanBridge() => CanBridge(NewCompSeen());

	private IOpticalComputationProvider? GetComputationProvider(ICollection<IOpticalComputationProvider> seen)
	{
		if (IsNetInvalidForTraversal()) return null;
		var inv = _net.GetNetData(_pipePos, _facing);
		if (inv == null) return null;
		var hatch = inv.GetComputationHatch();
		if (hatch == null || seen.Contains(hatch)) return null;
		return hatch;
	}

	private static ICollection<IDataAccessHatch> NewDataSeen() => new HashSet<IDataAccessHatch>();
	private static ICollection<IOpticalComputationProvider> NewCompSeen() => new HashSet<IOpticalComputationProvider>();
}
