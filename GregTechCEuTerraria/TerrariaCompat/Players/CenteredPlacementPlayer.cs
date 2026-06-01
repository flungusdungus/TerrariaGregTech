#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Players;

// Centers placement of our 2x2 tiles on the cursor pixel. Vanilla FLOORS
// `tileTargetX = (int)(MouseWorld / 16f)` (Player.cs:25120), so with Origin=(1,1)
// the visual center drifts up to ~16 px off-cursor. We ROUND instead, snapping
// to the nearest grid LINE = the 2x2's visual center.
//
// Timing gotcha: must hook PreItemCheck (after vanilla's tileTargetX assignment,
// before ItemCheck_Inner places) - PreUpdate fires BEFORE the reset and is lost.
// Only fires while holding a registered 2x2 item (CenteredPlacementTiles).
public sealed class CenteredPlacementPlayer : ModPlayer
{
	// Populated at Mod.Load by each 2x2 tile's SetStaticDefaults.
	public static readonly HashSet<int> CenteredPlacementTiles = new();

	public override bool PreItemCheck()
	{
		var held = Player.HeldItem;
		if (held is not null && !held.IsAir)
		{
			int createTile = held.createTile;
			if (createTile > 0 && CenteredPlacementTiles.Contains(createTile))
			{
				// Round (vanilla's floor + 0.5 bias) -> nearest grid line.
				Player.tileTargetX = (int)(Main.MouseWorld.X / 16f + 0.5f);
				Player.tileTargetY = (int)(Main.MouseWorld.Y / 16f + 0.5f);
			}
		}
		return true; // run vanilla ItemCheck normally
	}
}
