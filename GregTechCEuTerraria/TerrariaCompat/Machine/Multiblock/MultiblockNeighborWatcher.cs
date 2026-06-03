#nullable enable
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Forge `neighborChanged` bridge for formed multiblocks (no Terraria native)
public sealed class MultiblockNeighborWatcher : GlobalTile
{
	public override void PlaceInWorld(int i, int j, int type, Item item) => Notify(i, j);

	public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
	{
		if (fail || effectOnly) return;
		Notify(i, j);
	}

	private static void Notify(int x, int y)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;
		MultiblockControllerMachine.MarkStructureNeighborChanged(x, y);
	}
}

// World-unload reset (GlobalTile has no world-lifecycle hook; ModSystem does).
public sealed class MultiblockNeighborSystem : ModSystem
{
	public override void ClearWorld() => MultiblockControllerMachine.ClearFootprintRegistry();
}
