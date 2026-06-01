#nullable enable
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Pipenet;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;

// Verbatim port of LevelFluidPipeNet. Mark = material-id FNV-1a hash (same
// scheme as ItemPipeNetSystem) so different-material pipes stay separate.
public sealed class FluidPipeNetSystem : ModSystem
{
	private static LevelFluidPipeNet? _level;

	public static LevelFluidPipeNet Level
	{
		get
		{
			if (_level is null) _level = new LevelFluidPipeNet();
			return _level;
		}
	}

	public override void ClearWorld() { _level = new LevelFluidPipeNet(); }

	// DEVIATION: same as ItemPipeNetSystem - derived from cells,
	// no separate save. PipeNet hierarchy itself stays verbatim.
	public override void PostUpdateEverything() => MaybeRebuild();

	public override void PostUpdateWorld()
	{
		using (Profiler.Profiler.Time("tick", "fluid_pipe_net"))
		{
		MaybeRebuild();

		// Server-only (PostUpdateWorld is SP/server per tML); covers register
		// Update via ConditionalSubscriptionHandler.
		foreach (var pcv in FluidPipeLayerSystem.AllSides.Values)
			pcv.SystemTick();

		// Verbatim with upstream subscribeServerTick(this::update) on each
		// FluidPipeBlockEntity.
		foreach (var st in FluidPipeLayerSystem.AllStates.Values)
			st.Update();

		Profiler.Profiler.Gauge("counts", "fluid_pipe_states", FluidPipeLayerSystem.AllStates.Count);
		Profiler.Profiler.Gauge("counts", "fluid_pipe_sides",  FluidPipeLayerSystem.AllSides.Count);
		Profiler.Profiler.Gauge("counts", "fluid_pipe_nets",   Level.AllPipeNets.Count);
		}

		int syncPeriod = global::GregTechCEuTerraria.Config.GTConfig.Instance?.NetworkSyncPeriod ?? 6;
		if (Terraria.Main.netMode == Terraria.ID.NetmodeID.Server
			&& Terraria.Main.GameUpdateCount % (ulong)syncPeriod == 0)
			TerrariaCompat.Net.FluidPipeStatsPacket.Broadcast();
	}

	public static void MaybeRebuild()
	{
		if (!FluidPipeLayerSystem.Pipes.IsDirty) return;

		_level = new LevelFluidPipeNet();
		foreach (var kv in FluidPipeLayerSystem.Pipes.All)
		{
			FluidPipeLayerSystem.EnsureSides(kv.Key.x, kv.Key.y);
			OnPipeAdded(kv.Key.x, kv.Key.y, kv.Value);
			FluidPipeLayerSystem.EnsureState(kv.Key.x, kv.Key.y);
		}
		FluidPipeLayerSystem.Pipes.ClearDirty();
	}

	public static void OnPipeAdded(int x, int y, FluidPipeCell cell)
	{
		if (Level.GetNetFromPos((x, y)) is not null) return;

		var nodeData = new FluidPipeProperties
		{
			MaxFluidTemperature = cell.MaxFluidTemperature,
			Throughput          = cell.Throughput,
			Channels            = cell.Channels,
			GasProof            = cell.GasProof,
			CryoProof           = cell.CryoProof,
			PlasmaProof         = cell.PlasmaProof,
			AcidProof           = cell.AcidProof,
		};
		int mark = MaterialMark(cell.MaterialId);
		Level.AddNode((x, y), nodeData, mark, Node<FluidPipeProperties>.ALL_OPENED, isActive: false);
	}

	public static void OnPipeRemoved(int x, int y) => Level.RemoveNode((x, y));

	// FNV-1a, deterministic across runs (matches item-pipe MaterialMark).
	private static int MaterialMark(string id)
	{
		uint h = 2166136261u;
		for (int i = 0; i < id.Length; i++) { h ^= id[i]; h *= 16777619u; }
		return unchecked((int)(h | 0x80000000u));
	}
}

public sealed class LevelFluidPipeNet : LevelPipeNet<FluidPipeProperties, FluidPipeNet>
{
	protected internal override FluidPipeNet CreateNetInstance() => new(this);
}
