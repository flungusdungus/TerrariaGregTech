#nullable enable
using GregTechCEuTerraria.Api.Capability;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// LOCKED - port of EnergyNetWalker. Wire-to-endpoint connection is
// SAME-CELL only ("wire behind machine") so upstream's cardinal-neighbour
// checkNeighbour is replaced by CheckSelfPos; wire-to-wire walking stays
// cardinal (4-dir).
internal sealed class EnergyNetWalker : PipeNetWalker<CableCell, CableCell, CableLayer>
{
	public delegate bool TryGetEndpoint((int x, int y) pos, out IEnergyContainer ep);

	private readonly List<EnergyRoutePath> _routes;
	private readonly TryGetEndpoint _tryGetEndpoint;
	private List<(int x, int y)> _pipes;
	private long _loss;

	private EnergyNetWalker(
		CableLayer pipeNet,
		(int x, int y) sourcePipe,
		int walkedBlocks,
		List<EnergyRoutePath> routes,
		TryGetEndpoint tryGetEndpoint)
		: base(pipeNet, sourcePipe, walkedBlocks)
	{
		_routes = routes;
		_tryGetEndpoint = tryGetEndpoint;
		_pipes = new List<(int x, int y)>();
	}

	public static List<EnergyRoutePath> CreateNetData(
		CableLayer pipeNet,
		(int x, int y) sourcePipe,
		TryGetEndpoint tryGetEndpoint)
	{
		var routes = new List<EnergyRoutePath>();
		var walker = new EnergyNetWalker(pipeNet, sourcePipe, walkedBlocks: 1, routes, tryGetEndpoint);
		walker.TraversePipeNet();
		routes.Sort((a, b) => a.Distance.CompareTo(b.Distance));
		return routes;
	}

	protected override PipeNetWalker<CableCell, CableCell, CableLayer> CreateSubWalker(
		CableLayer pipeNet, IODirection facingToNextPos, (int x, int y) nextPos, int walkedBlocks)
	{
		// Copy pipes + loss per branch (verbatim with upstream).
		var walker = new EnergyNetWalker(pipeNet, nextPos, walkedBlocks, _routes, _tryGetEndpoint)
		{
			_pipes = new List<(int x, int y)>(_pipes),
			_loss = _loss,
		};
		return walker;
	}

	protected override void CheckPipe(CableCell pipeTile, (int x, int y) pos)
	{
		_pipes.Add(pos);
		_loss += pipeTile.LossPerAmp;
	}

	// Same-cell route; IODirection.None signals "internal" delivery.
	protected override void CheckSelfPos(CableCell pipeTile, (int x, int y) pos)
	{
		// Mirrors upstream's CheckNeighbour assert.
		if (_pipes.Count == 0 || _pipes[^1] != pos)
			throw new System.InvalidOperationException(
				"The current pipe is not the last added pipe. Something went seriously wrong!");

		if (!_tryGetEndpoint(pos, out var ep)) return;
		_routes.Add(new EnergyRoutePath(
			targetCablePos: pos,
			targetFacing:   IODirection.None,
			target:         ep,
			cables:         new List<(int x, int y)>(_pipes),
			distance:       WalkedBlocks,
			loss:           _loss));
	}

	protected override bool TryGetCellAt((int x, int y) pos, out CableCell cell)
	{
		var c = PipeNet.CellAt(pos.x, pos.y);
		if (c is null) { cell = default; return false; }
		cell = c.Value;
		return true;
	}
}
