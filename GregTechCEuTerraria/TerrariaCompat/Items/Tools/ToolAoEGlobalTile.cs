#nullable enable
using System;
using GregTechCEuTerraria.Api.Tool;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// AoE mining for GT drills + mining hammer + spade: breaks one extra LINE
// perpendicular to drilling direction (line, not upstream square - LV drill is
// 3x1). Scythes filtered here (not GTToolType.cs, keeping that upstream-verbatim).
//
// MP safety: _inAoE reentrancy guard stops local cascades; the tileTargetX/Y
// gate stops CROSS-client cascades - this KillTile fires on every recipient of a
// remote break broadcast, so without it two nearby miners re-expand each other's
// breaks forever. Gate restricts expansion to the LOCAL player's cursor cell.
public sealed class ToolAoEGlobalTile : GlobalTile
{
	private static bool _inAoE;

	public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
	{
		if (_inAoE || fail || effectOnly) return;
		if (Main.netMode == NetmodeID.Server) return;

		var player = Main.LocalPlayer;
		if (player is null || player.itemAnimation <= 0) return;
		if (player.HeldItem?.ModItem is not ToolItem tool) return;
		if (tool.ToolType == GTToolType.SCYTHE) return;

		// Cross-client cascade gate - see header.
		if (i != Player.tileTargetX || j != Player.tileTargetY) return;

		int reach = Math.Max(tool.AoeColumn, tool.AoeRow);
		if (reach <= 0) return;
		if (tool.IsElectric && tool.GetCharge() <= 0) return;

		float dx = (i + 0.5f) * 16f - player.Center.X;
		float dy = (j + 0.5f) * 16f - player.Center.Y;
		bool miningHorizontal = Math.Abs(dx) >= Math.Abs(dy);

		_inAoE = true;
		for (int k = -reach; k <= reach; k++)
		{
			if (k == 0) continue;
			int nx = miningHorizontal ? i : i + k;
			int ny = miningHorizontal ? j + k : j;
			if (!WorldGen.InWorld(nx, ny, 10)) continue;
			if (!Main.tile[nx, ny].HasTile) continue;
			// Respect per-tool restrictions (e.g. soft-digger filter in
			// ToolWorldEffects.CanKillTile).
			if (!WorldGen.CanKillTile(nx, ny)) continue;

			WorldGen.KillTile(nx, ny);
			if (Main.netMode == NetmodeID.MultiplayerClient && !Main.tile[nx, ny].HasTile)
				NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, nx, ny);
		}
		_inAoE = false;
	}
}
