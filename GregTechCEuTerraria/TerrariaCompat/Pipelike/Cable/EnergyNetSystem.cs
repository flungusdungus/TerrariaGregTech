#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.Api.Capability;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// Orchestrates the energy net: rebuilds connected components when the cable
// layer changes, links endpoints via "wire behind machine" same-cell adjacency
// (see LinkEndpoints), ticks every network in PostUpdateWorld.
public sealed class EnergyNetSystem : ModSystem
{
	private static readonly List<EnergyNet> _networks = new();
	private static readonly Dictionary<(int x, int y), EnergyNet> _byCell = new();
	private static readonly Dictionary<(int x, int y), IEnergyContainer> _endpoints = new();
	private static bool _endpointsDirty;

	// AnchorCell-keyed; populated by EnergyNetStatsPacket so wire-hover
	// tooltips on MP clients show real throughput (never-ticked client net
	// would otherwise read zeros).
	private static readonly Dictionary<(int x, int y), (long extracted, long delivered)> _clientStats = new();
	public  static Dictionary<(int x, int y), (long extracted, long delivered)> ClientStats => _clientStats;

	public static int NetCount => _networks.Count;
	public static IReadOnlyList<EnergyNet> Nets => _networks;
	public static EnergyNet? NetAt(int x, int y) =>
		_byCell.TryGetValue((x, y), out var n) ? n : null;

	// Server reads live fields; client reads the cache. (0,0) when idle or
	// during the first 6 ticks after a topology change (cache lag).
	public static (long extracted, long delivered) GetThroughput(EnergyNet net)
	{
		if (TerrariaCompat.Machine.MetaMachine.IsClient)
		{
			if (_clientStats.TryGetValue(net.AnchorCell, out var stats)) return stats;
			return (0, 0);
		}
		return (net.LastTickExtracted, net.LastTickDelivered);
	}

	// Rebuild() re-discovers endpoints from TileEntity.ByID; these are kept
	// as a fast-path for ad-hoc registration.
	public static void RegisterEndpoint(int x, int y, IEnergyContainer container)
	{
		_endpoints[(x, y)] = container;
		_endpointsDirty = true;
	}

	public static void UnregisterEndpoint(int x, int y)
	{
		if (_endpoints.Remove((x, y)))
			_endpointsDirty = true;
	}

	// Used when a machine's I/O role changes without endpoint set OR cable
	// layer changing - e.g. transformer direction flip swaps producer/consumer.
	public static void MarkEndpointsDirty() => _endpointsDirty = true;

	public override void OnWorldLoad()
	{
		_endpoints.Clear();
		foreach (var kv in TileEntity.ByID)
			RegisterEndpointCells(kv.Value);
		_endpointsDirty = true;
	}

	private static void RegisterEndpointCells(TileEntity te)
	{
		if (te is not IEnergyContainer container) return;
		// Filter out ILaserContainer - laser hatches route exclusively through
		// the laser pipenet, NOT the wire/cable net. Upstream enforces this via
		// separate Forge capability tokens (CAPABILITY_ENERGY vs CAPABILITY_LASER);
		// our ILaserContainer extends IEnergyContainer for code reuse, so we
		// have to filter explicitly here. Without this filter, an IV laser hatch
		// (8192V emitter) sitting on a wire adjacent to a lower-tier energy hatch
		// (e.g. EV InputVoltage 2048) makes the wire-net push IV-voltage at the
		// EV consumer, triggering over-voltage explosion.
		if (container is ILaserContainer) return;
		if (te is TerrariaCompat.Machine.MetaMachine machine)
		{
			foreach (var (cx, cy) in machine.Cells())
				_endpoints[(cx, cy)] = container;
		}
		else
		{
			_endpoints[(te.Position.X, te.Position.Y)] = container;
		}
	}

	private static int _lastEntityCount = -1;

	// Default cadence (~10 Hz at 60 fps); overridden per-tick by config.
	// Fallback covers the brief window before Config loads.
	private const int DefaultStateSyncPeriod = 6;
	private static int StateSyncPeriod =>
		global::GregTechCEuTerraria.Config.GTConfig.Instance?.NetworkSyncPeriod ?? DefaultStateSyncPeriod;
	private static int _stateSyncCounter;

	// Rebuild trigger - runs on BOTH server (via PostUpdateWorld) and client
	// (via PostUpdateEverything). The network graph is a pure function of
	// CableLayer + TileEntity.ByID, both of which exist on clients (cable
	// layer sync'd via CablePackets, TEs via TileEntitySharing). Running
	// Rebuild on the client keeps `_byCell` populated so the wire-hover
	// tooltip can resolve the network it sits on. On the client the per-link
	// Producer/Consumer classification may be stale until the next
	// state-sync arrives, but the component shape (cells, effective tier /
	// amperage / loss) is accurate and that's what the tooltip needs.
	//
	// tML lifecycle gotcha: `ModSystem.PostUpdateWorld` is documented
	// "Called in single player or on the server only" - it's dispatched from
	// `WorldGen.UpdateWorld`, which `Main` skips on MP clients (netMode==1).
	// `PostUpdateEverything` is the one documented to run "on all clients
	// and the server", so the client-side rebuild has to live there.
	// Calling it from both hooks is idempotent: after a successful rebuild
	// the dirty flags are cleared and the second call is a cheap no-op.
	public static void MaybeRebuild()
	{
		// Cheap entity-churn check (place/break) - force rebuild on count change.
		int currentCount = TileEntity.ByID.Count;
		if (currentCount != _lastEntityCount)
		{
			_lastEntityCount = currentCount;
			_endpointsDirty = true;
		}

		if (CableLayerSystem.Cables.IsDirty || _endpointsDirty)
		{
			using (Profiler.Profiler.Time("tick", "energy_net_rebuild"))
				Rebuild();
			CableLayerSystem.Cables.ClearDirty();
			_endpointsDirty = false;
		}
	}

	public override void PostUpdateEverything()
	{
		MaybeRebuild();

		// Client-side per-tick trait advance. Required because SaveForSync
		// omits per-tick monotonic fields (recipe progress) from the wire
		// blob, so dirty-skip suppresses the broadcast between status
		// transitions. The client interpolates locally via this hook.
		// Server doesn't need it - traits subscribe via the trait holder
		// and tick from the machine SystemTick walk in PostUpdateWorld.
		if (TerrariaCompat.Machine.MetaMachine.IsClient)
		{
			foreach (var te in TileEntity.ByID.Values)
			{
				if (te is TerrariaCompat.Machine.MetaMachine machine)
					machine.OnClientPostUpdate();
			}
		}
	}

	public override void PostUpdateWorld()
	{
		using var _energyNetScope = Profiler.Profiler.Time("tick", "energy_net_total");
		MaybeRebuild();

		// Server-authoritative: state-sync packets are the client's ONLY
		// per-tick update path (NetReceive only fires on placement / chunk).
		if (TerrariaCompat.Machine.MetaMachine.IsClient) return;

		// Belts-and-suspenders SystemTick walk in case ModTileEntity.Update
		// isn't firing for our types. Covers steam producers too (they share
		// the SystemTick path via MetaMachine).
		int machineCount = 0;
		using (Profiler.Profiler.Time("tick", "machine_systemtick"))
		{
			foreach (var te in TileEntity.ByID.Values)
			{
				if (te is TerrariaCompat.Machine.MetaMachine machine)
				{
					// Per-type bucketing: we already pay the GetType().Name string
					// allocation for sync-bytes-by-type elsewhere, so reuse the
					// same key. AccumulateTimer keeps the hot path to a dict
					// lookup + long add (no TimerScope ctor).
					long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
					// Per-entity isolation: a machine throwing inside its tick
					// (e.g. a bad recipe-data read) must NOT abort the whole
					// PostUpdateWorld - that silently stops ticking every entity
					// AFTER it in ByID order (recipe progress, AsyncCheckPattern
					// re-form, fused-casing reskin all freeze). Mirrors the
					// try/catch already guarding the BroadcastNearby loop.
					try { machine.SystemTick(); }
					catch (System.Exception ex)
					{
						ModContent.GetInstance<GregTechCEuTerraria>()?.Logger.Warn(
							$"[SystemTick] ({machine.Position.X},{machine.Position.Y}) " +
							$"{machine.GetType().Name} threw - isolated, continuing", ex);
					}
					long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
					Profiler.Profiler.AccumulateTimer(
						"tick.machine_systemtick.by_type", machine.GetType().Name, elapsed);
					machineCount++;
				}
			}
		}
		Profiler.Profiler.Gauge("counts", "machines", machineCount);
		Profiler.Profiler.Gauge("counts", "energy_networks", _networks.Count);

		// Gate wire-net push to upstream's 20 Hz MC cadence. Each `Tick()`
		// pull-walks producers and pays out `GetPushAmperage()` ampere-units;
		// firing every Terraria tick (60 Hz) would deliver 3x upstream's
		// design rate per real second, breaking the recipe / generator
		// balance the same way unguarded `_progress++` does in OnTick.
		// Same `% FromMcTicks(1)` shape as MetaMachine.SystemTick:285 +
		// NotifiableEnergyContainer.ServerTick.
		if (Main.GameUpdateCount % (uint)global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(1) == 0)
		{
			using (Profiler.Profiler.Time("tick", "energy_net_simulate"))
			{
				foreach (var net in _networks)
					net.Tick();
			}
		}

		// Periodic state-sync broadcast (server only).
		// Targets = GUI viewers union players within NearbyRadiusPx.
		//
		// STAGGERED across the period: the loop runs every tick, but each machine
		// only broadcasts on the tick where (GameUpdateCount + posPhase) % period
		// == 0 - so every machine still syncs once per `period` ticks, but the
		// expensive per-machine SaveDataForSync serialize is spread evenly over
		// the period instead of all ~N machines bunching on one tick (the 318 ms
		// `machine_state_broadcast` spike). Same total work, no spike.
		if (Main.netMode == Terraria.ID.NetmodeID.Server)
		{
			int period = StateSyncPeriod;
			if (period < 1) period = 1;

			// Net-wide stats packet stays on the global cadence (one packet, not
			// per-machine - nothing to stagger).
			if (++_stateSyncCounter >= period)
			{
				_stateSyncCounter = 0;
				TerrariaCompat.Net.EnergyNetStatsPacket.Broadcast();
			}

			using var _broadcastScope = Profiler.Profiler.Time("tick", "machine_state_broadcast");
			uint gt = Main.GameUpdateCount;
			foreach (var te in TileEntity.ByID.Values)
			{
				if (te is not TerrariaCompat.Machine.MetaMachine machine) continue;

				// Position-hash phase so neighbours don't all fire on the same tick.
				uint phase = ((uint)machine.Position.X * 2654435761u
				            + (uint)machine.Position.Y * 40503u) % (uint)period;
				if ((gt + phase) % (uint)period != 0) continue;

				//  ModPacket.Send throws past 65535 bytes; without per-entity
				// isolation ONE oversize entity poisons the rest of the iteration
				// in ByID order (the "multi parts not reskinned" MP bug). Cap-at-
				// source (in RecipeLogic.Save) prevents it today; catch stays as
				// a defense.
				try
				{
					machine.PruneViewers();
					TerrariaCompat.Net.MachineStateSyncPacket.BroadcastNearby(machine);
					// Compact energy channel (energy is omitted from the blob above).
					// Cheap long-compare dirty-skip; only sends when energy changed.
					TerrariaCompat.Net.MachineEnergySyncPacket.BroadcastNearby(machine);
					if (machine.HasViewers)
						TerrariaCompat.Net.EnderChannelSyncPacket.Broadcast(machine);
				}
				catch (System.Exception ex)
				{
					Terraria.ModLoader.ModLoader.GetMod("GregTechCEuTerraria")?.Logger?.Error(
						$"[StateSync] entity ({machine.Position.X},{machine.Position.Y}) " +
						$"{machine.GetType().Name} threw during broadcast - isolated, iteration continues: " +
						$"{ex.GetType().Name}: {ex.Message}");
				}
			}
		}
	}

	public override void ClearWorld()
	{
		_networks.Clear();
		_byCell.Clear();
		_endpoints.Clear();
		_clientStats.Clear();
	}

	private static void Rebuild()
	{
		_networks.Clear();
		_byCell.Clear();

		// Multi-tile machines get registered at every footprint cell so a
		// cable adjacent to ANY cell finds the container.
		_endpoints.Clear();
		foreach (var te in TileEntity.ByID.Values)
			RegisterEndpointCells(te);

		var components = EnergyNetGraph.Build(CableLayerSystem.Cables);

		foreach (var comp in components)
		{
			var net = new EnergyNet(comp);
			_networks.Add(net);
			foreach (var pos in comp.Cells.Keys)
				_byCell[pos] = net;

			LinkEndpoints(net, comp);
		}

	}

	// SAME-CELL connectivity model ("wire behind machine"). Cardinal-adjacent
	// endpoints do NOT connect - player routes the wire under the machine.
	internal static void LinkEndpoints(EnergyNet net, NetworkComponent comp)
	{
		var seenProducers = new HashSet<IEnergyContainer>();
		var seenConsumers = new HashSet<IEnergyContainer>();
		foreach (var cell in comp.Cells.Keys)
		{
			TryLink(net, seenProducers, seenConsumers, cell.x, cell.y, cell.x, cell.y);
		}
		net.SetEndpointLookup(pos => _endpoints.TryGetValue(pos, out var ep) ? ep : null);
	}

	private static void TryLink(EnergyNet net,
		HashSet<IEnergyContainer> seenProducers,
		HashSet<IEnergyContainer> seenConsumers,
		int epX, int epY, int cableX, int cableY)
	{
		if (!_endpoints.TryGetValue((epX, epY), out var ep)) return;
		// Sideless machines return None; OutputsEnergy/InputsEnergy on None
		// degrade to flat OutputVoltage>0 / InputVoltage>0 checks.
		var face = ep.EnergyFaceForCell(epX, epY);
		if (ep.OutputsEnergy(face))
		{
			net.ProducerLinks.Add((cableX, cableY, ep));
			if (seenProducers.Add(ep)) net.Producers.Add(ep);
		}
		if (ep.InputsEnergy(face))
		{
			net.ConsumerLinks.Add((cableX, cableY, ep));
			if (seenConsumers.Add(ep)) net.Consumers.Add(ep);
		}
	}

	internal static void TestOnly_BuildAndLink(CableLayer layer, Dictionary<(int, int), IEnergyContainer> endpoints, List<EnergyNet> output)
	{
		var prev = new Dictionary<(int, int), IEnergyContainer>(_endpoints);
		_endpoints.Clear();
		foreach (var kv in endpoints) _endpoints[kv.Key] = kv.Value;
		try
		{
			output.Clear();
			var components = EnergyNetGraph.Build(layer);
			foreach (var comp in components)
			{
				var net = new EnergyNet(comp);
				output.Add(net);
				LinkEndpoints(net, comp);
			}
		}
		finally
		{
			_endpoints.Clear();
			foreach (var kv in prev) _endpoints[kv.Key] = kv.Value;
		}
	}
}
