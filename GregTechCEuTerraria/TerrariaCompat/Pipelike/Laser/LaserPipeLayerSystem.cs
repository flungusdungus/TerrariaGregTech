#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// Per-world LaserPipeLayer. Mirrors ItemPipeLayerSystem / CableLayerSystem
// lifecycle - owns the GridLayer, persists cells via Save/LoadWorldData,
// drives the renderer.
//
// Also owns the per-cell "active" tick map - mirrors upstream `LaserPipe
// BlockEntity.setActive(true, 100)` which sets a block-state property for
// N ticks to drive the active glow. Our equivalent is a (x,y) -> expiry-tick
// dictionary that the renderer reads.
public sealed class LaserPipeLayerSystem : ModSystem
{
	public static LaserPipeLayer Pipes { get; } = new();

	// (x, y) -> GameUpdateCount at which the active flag expires. Cells not
	// present in the map are inactive. Touched server-side by
	// `LaserNetHandler.SetPipesActive`; touched client-side by the active-
	// state sync packet (not yet wired - placeholder for parity with
	// upstream's clientside block-state sync).
	private static readonly Dictionary<(int x, int y), uint> _activeUntil = new();

	public static bool IsActive(int x, int y) =>
		_activeUntil.TryGetValue((x, y), out var until) && until > Main.GameUpdateCount;

	public static void SetActive(int x, int y, int ticks)
	{
		_activeUntil[(x, y)] = Main.GameUpdateCount + (uint)ticks;
	}

	public override void Load()
	{
		On_Main.DoDraw_WallsAndBlacks += DrawAfterWalls;
	}

	public override void Unload()
	{
		On_Main.DoDraw_WallsAndBlacks -= DrawAfterWalls;
		Pipes.Clear();
		_activeUntil.Clear();
	}

	private static void DrawAfterWalls(On_Main.orig_DoDraw_WallsAndBlacks orig, Main self)
	{
		orig(self);
		LaserPipeRenderer.DrawLaserPipes();
	}

	public override void ClearWorld()
	{
		Pipes.Clear();
		_activeUntil.Clear();
	}

	// MP client late-join: request the full layer dump from the server.
	// SP no-op (PipePackets.SendLayerRequest gates on netMode). Mirrors
	// ItemPipeLayerSystem / FluidPipeLayerSystem / CableLayerSystem.
	public override void OnWorldLoad()
	{
		Net.PipePackets.SendLayerRequest(PipeKind.Laser);
	}

	// Held-item place-preview overlay (matches ItemPipeLayerSystem.PostDrawTiles).
	public override void PostDrawTiles()
	{
		var held = Main.LocalPlayer?.HeldItem;
		if (held?.ModItem is Items.Pipes.LaserPipeItem)
			LaserPipeRenderer.DrawLaserForegroundOverlay();
	}

	public override void SaveWorldData(TagCompound tag)
	{
		if (Pipes.Count == 0) return;
		var xs = new List<int>(Pipes.Count);
		var ys = new List<int>(Pipes.Count);
		var op = new List<int>(Pipes.Count);
		foreach (var kv in Pipes.All)
		{
			xs.Add(kv.Key.x);
			ys.Add(kv.Key.y);
			op.Add(kv.Value.Open);
		}
		tag["laser_pipes.xs"] = xs;
		tag["laser_pipes.ys"] = ys;
		tag["laser_pipes.open"] = op;
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Pipes.Clear();
		_activeUntil.Clear();
		if (!tag.ContainsKey("laser_pipes.xs")) return;
		var xs = tag.GetList<int>("laser_pipes.xs");
		var ys = tag.GetList<int>("laser_pipes.ys");
		var op = tag.ContainsKey("laser_pipes.open") ? tag.GetList<int>("laser_pipes.open") : null;
		int n = System.Math.Min(xs.Count, ys.Count);
		for (int i = 0; i < n; i++)
		{
			byte open = (op != null && i < op.Count) ? (byte)op[i] : (byte)0;
			Pipes.Set(xs[i], ys[i], new LaserPipeCell { Open = open });
		}
	}
}
