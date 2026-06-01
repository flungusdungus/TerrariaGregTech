#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// Verbatim port of com.gregtechceu.gtceu.common.pipelike.laser.LaserNetHandler.
//
// Per-side ILaserContainer the laser pipe exposes outward. When an adjacent
// laser-aware endpoint pushes EU through the pipe's face, this handler
// resolves the unique destination via the net's route cache and delegates
// every read/write to it. Lossless point-to-point.
//
// === Documented adaptations =================================================
//
//   - `LaserPipeBlockEntity pipe` -> `(int x, int y) pipePos` + axis-side
//     `_facing` field. We don't have a per-pipe tile entity; the layer's
//     cell is the source of truth.
//   - `pipe.isBlocked(facing)` check DROPPED - our laser pipe cells are
//     never "blocked" per-side (no cover system on laser pipes).
//   - `setActive(true, 100)` on the route -> tracked via per-cell tick
//     counter in `LaserPipeLayerSystem.SetActive(pos, ticks)` - rendering
//     consumer.
public sealed class LaserNetHandler : ILaserContainer
{
	private LaserPipeNet _net;
	private readonly (int x, int y) _pipePos;
	private readonly IODirection    _facing;

	public LaserNetHandler(LaserPipeNet net, (int x, int y) pipePos, IODirection facing)
	{
		_net     = net;
		_pipePos = pipePos;
		_facing  = facing;
	}

	public void UpdateNetwork(LaserPipeNet net) => _net = net;
	public LaserPipeNet GetNet() => _net;

	// Mirrors upstream `setPipesActive` - walks every pipe in the net and
	// lights it for `100` server ticks (~5s real time). Used to drive the
	// "energy flowing" visual.
	private void SetPipesActive()
	{
		foreach (var pos in _net.AllNodes.Keys)
			LaserPipeLayerSystem.SetActive(pos.x, pos.y, ticks: 100);
	}

	// Verbatim with upstream getInnerContainer - returns the unique endpoint
	// via the net's route cache. Null when no walk has resolved yet (or the
	// walker failed and won't cache).
	private ILaserContainer? GetInnerContainer()
	{
		if (_net == null || _facing == IODirection.None) return null;
		var data = _net.GetNetData(_pipePos, _facing);
		return data?.GetHandler();
	}

	public long AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage)
	{
		var handler = GetInnerContainer();
		if (handler == null) return 0L;
		SetPipesActive();
		return handler.AcceptEnergyFromNetwork(side, voltage, amperage);
	}

	public bool InputsEnergy(IODirection side)
	{
		var handler = GetInnerContainer();
		return handler != null && handler.InputsEnergy(side);
	}

	public bool OutputsEnergy(IODirection side)
	{
		var handler = GetInnerContainer();
		return handler != null && handler.OutputsEnergy(side);
	}

	public long ChangeEnergy(long amount)
	{
		var handler = GetInnerContainer();
		if (handler == null) return 0L;
		SetPipesActive();
		return handler.ChangeEnergy(amount);
	}

	public long EnergyStored   => GetInnerContainer()?.EnergyStored   ?? 0L;
	public long EnergyCapacity => GetInnerContainer()?.EnergyCapacity ?? 0L;

	// Upstream returns 0 for both - the handler is a pure pass-through; voltage
	// and amperage are upstream's concept of "what this container can pull"
	// (we're a pipe, not an endpoint).
	public long InputVoltage   => 0L;
	public long InputAmperage  => 0L;
}
