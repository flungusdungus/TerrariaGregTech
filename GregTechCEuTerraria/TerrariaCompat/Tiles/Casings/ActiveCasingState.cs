#nullable enable
using System.Collections.Generic;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Casings;

// Casing cells currently in an active (recipe-running) multi - drives the face-
// swap in CasingTile.PreDraw (anchor in set -> ActiveBlockTexture). Server-
// authoritative, broadcast via ActiveCasingPacket; clients hold a derived view
// (no save/load, rebuilt on sync + late-join). Anchors are 2x2-cell tile coords
// (the space MultiblockState.GetCache uses), packed into a long.
public sealed class ActiveCasingState : ModSystem
{
	private static readonly HashSet<long> _activeCells = new();

	public override void OnWorldUnload() => _activeCells.Clear();

	// MP late-join: request the authoritative set (no-op in SP / dedi-server).
	public override void OnWorldLoad() =>
		TerrariaCompat.Net.ActiveCasingPacket.SendRequest();

	private static long Pack(int x, int y) => ((long)y << 32) | (uint)x;

	public static bool IsActive(int x, int y) => _activeCells.Contains(Pack(x, y));

	public static void SetActive(int x, int y) => _activeCells.Add(Pack(x, y));
	public static void ClearActive(int x, int y) => _activeCells.Remove(Pack(x, y));

	public static void SetActive(IEnumerable<Point16> cells)
	{
		foreach (var p in cells) _activeCells.Add(Pack(p.X, p.Y));
	}

	public static void ClearActive(IEnumerable<Point16> cells)
	{
		foreach (var p in cells) _activeCells.Remove(Pack(p.X, p.Y));
	}

	// Used by ActiveCasingPacket to dump the full set on late-join.
	public static IEnumerable<Point16> AllActiveCells()
	{
		foreach (var packed in _activeCells)
			yield return new Point16((int)(packed & 0xFFFFFFFF), (int)(packed >> 32));
	}

	public static int Count => _activeCells.Count;
}
