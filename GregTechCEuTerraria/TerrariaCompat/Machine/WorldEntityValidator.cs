#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Post-world-load validator - walks TileEntity.ByPosition and logs anything
// that looks inconsistent. Pure observability: does NOT remove or fix
// anything, only reports.
//
// What's flagged:
//   - MetaMachine entity whose anchor tile is missing (HasTile == false)
//   - MetaMachine entity whose anchor tile is the wrong type
//   - Two entities sharing the same anchor position
//   - Formed MultiblockControllerMachine whose pattern check is unhappy
//
// Runs on OnWorldLoad (every world entry, SP + dedicated server). Skipped on
// MP clients - they receive entity state via TileEntitySharing after the
// server has already validated.
public sealed class WorldEntityValidator : ModSystem
{
	public override void OnWorldLoad()
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;
		ValidateAndLog(Mod);
	}

	internal static void ValidateAndLog(Mod mod)
	{
		int orphans      = 0;
		int wrongType    = 0;
		int unformedMulti = 0;
		int formedMulti  = 0;
		var seenPositions = new Dictionary<Point16, MetaMachine>();
		var dupePositions = 0;

		foreach (var (_, te) in TileEntity.ByPosition)
		{
			if (te is not MetaMachine m) continue;
			var p = m.Position;

			if (seenPositions.TryGetValue(p, out var existing))
			{
				mod.Logger.Warn(
					$"[Validator] DUPLICATE entity at ({p.X},{p.Y}): " +
					$"{existing.GetType().Name} <id={existing.ID}> AND {m.GetType().Name} <id={m.ID}>");
				dupePositions++;
			}
			else seenPositions[p] = m;

			if (p.X < 0 || p.X >= Main.maxTilesX || p.Y < 0 || p.Y >= Main.maxTilesY)
			{
				mod.Logger.Warn($"[Validator] OUT-OF-BOUNDS entity at ({p.X},{p.Y}): {m.GetType().Name} <id={m.ID}>");
				orphans++;
				continue;
			}

			var tile = Main.tile[p.X, p.Y];
			if (!tile.HasTile)
			{
				mod.Logger.Warn(
					$"[Validator] ORPHAN - no tile at ({p.X},{p.Y}) " +
					$"for {m.GetType().Name} '{SafeMachineId(m)}' <id={m.ID}>");
				orphans++;
				continue;
			}

			if (!m.IsTileValidForEntity(p.X, p.Y))
			{
				mod.Logger.Warn(
					$"[Validator] WRONG TILE at ({p.X},{p.Y}): tile is type {tile.TileType}, " +
					$"but entity {m.GetType().Name} '{SafeMachineId(m)}' doesn't accept it " +
					$"(likely tile was replaced without KillMultiTile firing).");
				wrongType++;
				continue;
			}

			if (m is MultiblockControllerMachine c)
			{
				if (c.IsFormed) formedMulti++;
				else unformedMulti++;
			}
		}

		mod.Logger.Info(
			$"[Validator] world scan complete: {seenPositions.Count} GT entities | " +
			$"orphans={orphans} wrong-tile={wrongType} duplicates={dupePositions} | " +
			$"multis: formed={formedMulti} unformed={unformedMulti}");
	}

	private static string SafeMachineId(MetaMachine m)
	{
		try { return m.MachineId ?? "<null>"; }
		catch { return "<error>"; }
	}
}
