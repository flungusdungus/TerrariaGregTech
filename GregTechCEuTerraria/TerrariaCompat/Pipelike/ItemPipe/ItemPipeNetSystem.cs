#nullable enable
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// Concrete LevelPipeNet for item pipes. Mark = FNV-1a hash of MaterialId
// (same-material pipes share a net; FNV-1a is deterministic across runs so
// saved marks round-trip without overriding Node serialization). Upstream
// uses paint colour as the discriminator; we dropped paint project-wide.
public sealed class ItemPipeNetSystem : ModSystem
{
	private static LevelItemPipeNet? _level;

	// Populated by PipeStatsPacket; read by the pipe panel on MP clients.
	public static readonly System.Collections.Generic.Dictionary<(int x, int y), int>
		ClientTransferStats = new();

	public static LevelItemPipeNet Level
	{
		get
		{
			if (_level is null) _level = new LevelItemPipeNet();
			return _level;
		}
	}

	public override void ClearWorld() { _level = new LevelItemPipeNet(); }

	// DEVIATION (consistency-driven): upstream's LevelPipeNet
	// persists the net via SavedData; we mirror cable's "derived from cells,
	// no separate save" model. The PipeNet hierarchy itself stays verbatim.
	// PostUpdateEverything runs on MP clients (PostUpdateWorld doesn't).
	public override void PostUpdateEverything() => MaybeRebuild();

	public override void PostUpdateWorld()
	{
		using (Profiler.Profiler.Time("tick", "item_pipe_net"))
		{
		MaybeRebuild();

		// Server-only walk (PostUpdateWorld is SP/server per tML doc); covers
		// register their Update via ConditionalSubscriptionHandler here.
		foreach (var pcv in ItemPipeLayerSystem.AllSides.Values)
			pcv.SystemTick();

		Profiler.Profiler.Gauge("counts", "item_pipe_sides", ItemPipeLayerSystem.AllSides.Count);
		Profiler.Profiler.Gauge("counts", "item_pipe_nets",  Level.AllPipeNets.Count);
		}

		// NOT scaled by SimulationSpeed - net pacing, not gameplay timing.
		int syncPeriod = global::GregTechCEuTerraria.Config.GTConfig.Instance?.NetworkSyncPeriod ?? 6;
		if (Main.netMode == NetmodeID.Server && Main.GameUpdateCount % syncPeriod == 0)
			PipeStatsPacket.Broadcast();
	}

	public static void MaybeRebuild()
	{
		if (!ItemPipeLayerSystem.Pipes.IsDirty) return;

		_level = new LevelItemPipeNet();
		foreach (var kv in ItemPipeLayerSystem.Pipes.All)
		{
			// Every cell needs a PipeCoverable so the panel's Last-20t counter
			// has somewhere to read from.
			ItemPipeLayerSystem.EnsureSides(kv.Key.x, kv.Key.y);
			OnPipeAdded(kv.Key.x, kv.Key.y, kv.Value);
		}
		ItemPipeLayerSystem.Pipes.ClearDirty();
	}

	public static void OnPipeAdded(int x, int y, ItemPipeCell cell)
	{
		// Idempotent - lets world-load callers replay safely.
		if (Level.GetNetFromPos((x, y)) is not null) return;

		var nodeData = new ItemPipeProperties(cell.Priority, cell.TransferRate);
		int mark = MaterialMark(cell.MaterialId);
		// All sides open; per-side gating lives on PipeCoverable's cover.
		// PipeNet's blocked-connections bitmask gates merging, not transport.
		Level.AddNode((x, y), nodeData, mark, Node<ItemPipeProperties>.ALL_OPENED, isActive: false);
	}

	public static void OnPipeRemoved(int x, int y) => Level.RemoveNode((x, y));

	private static int MaterialMark(string id)
	{
		uint h = 2166136261u;
		for (int i = 0; i < id.Length; i++) { h ^= id[i]; h *= 16777619u; }
		// Force high bit to avoid mark==0 (DEFAULT_MARK = over-merge).
		return unchecked((int)(h | 0x80000000u));
	}
}

public sealed class LevelItemPipeNet : LevelPipeNet<ItemPipeProperties, ItemPipeNet>
{
	protected internal override ItemPipeNet CreateNetInstance() => new(this);
}
