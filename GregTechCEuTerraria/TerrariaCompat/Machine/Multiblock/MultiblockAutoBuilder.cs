#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Pattern;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Adapted port of BlockPattern.autoBuild (Terminal's structure builder). The
// candidate cascade + global/layer count bookkeeping are 1:1 upstream; placement
// + inventory-consume are Terraria glue.
//
// MP: runs on the placing client (or SP). Each cell PlaceObjects locally +
// SendTileSquare; part entities go through MachinePlacedPacket (server-auth).
// Inventory consumed locally + client->server SyncEquipment - dupe-proof (no
// server->client echo to trip the ignore-self gate).
//
// 2D deviations: flat string[] (no aisle/rotation/flip), no resetFacing, no
// terrain-clearing (partial build = upstream's place-FAIL -> continue).
// Formation runs in the controller's AsyncCheckPattern, not here.
public static class MultiblockAutoBuilder
{
	public static bool Build(MultiblockControllerMachine controller, Player player)
	{
		if (controller.IsFormed) return false;
		var pattern = controller.GetPattern();
		if (pattern is null) return false;
		var preview = pattern.GetPreviewPattern();

		int originX = controller.Position.X - preview.ControllerCol * 2;
		int originY = controller.Position.Y - preview.ControllerRow * 2;

		var state = new MultiblockState(controller.Position.X, controller.Position.Y);
		state.Clean();

		bool placedAny = false;

		for (int row = 0; row < preview.Height; row++)
		{
			// = upstream cacheLayer.clear() per aisle.
			state.LayerCount.Clear();
			for (int col = 0; col < preview.Width; col++)
			{
				char ch = preview.Shape[row][col];
				if (!preview.Predicates.TryGetValue(ch, out var predicate))
					continue;

				int tileX = originX + col * 2;
				int tileY = originY + row * 2;
				if (!state.Update(tileX, tileY, predicate))
					continue;

				if (Main.tile[tileX, tileY].HasTile)
				{
					// Bump count caches so already-satisfied required cells aren't
					// re-counted as "still needed" - verbatim upstream testLimited.
					foreach (var limit in predicate.Limited)
						limit.TestLimited(state);
					continue;
				}

				var options = BuildPlacementOptions(predicate, state);
				int slot = -1;
				SimplePredicate? chosenLimited = null;
				foreach (var (types, limited) in options)
				{
					slot = FirstInventorySlot(player, types);
					if (slot >= 0) { chosenLimited = limited; break; }
				}
				if (slot < 0) continue;

				int tileType = player.inventory[slot].createTile;
				if (tileType == -1) continue;

				if (!TryPlaceCellTile(tileX, tileY, (ushort)tileType))
					continue;

				// Count only when a limited (special) part was placed - a casing
				// fallback doesn't satisfy the special's requirement.
				if (chosenLimited is not null)
				{
					state.LayerCount[chosenLimited]  = GetCount(state.LayerCount, chosenLimited) + 1;
					state.GlobalCount[chosenLimited] = GetCount(state.GlobalCount, chosenLimited) + 1;
				}

				ConsumeOne(player, slot);
				placedAny = true;
			}
		}

		return placedAny;
	}

	// DEVIATION from upstream autoBuild: upstream
	// reserves a required-min cell for its special part and leaves it EMPTY when
	// the player lacks one (the count bump happens BEFORE the inventory check).
	// We instead build the full ordered list - special parts first (so a held
	// hatch is still placed) then casing as fallback - and the caller counts a
	// limited part toward its min/max only when it's the one actually placed.
	private static List<(List<int> Types, SimplePredicate? Limited)> BuildPlacementOptions(
		TraceabilityPredicate predicate, MultiblockState state)
	{
		var options = new List<(List<int>, SimplePredicate?)>();
		var added = new HashSet<SimplePredicate>();

		void AddLimited(SimplePredicate limit)
		{
			if (!added.Add(limit)) return;
			if (limit.MaxLayerCount != -1 && GetCount(state.LayerCount, limit) >= limit.MaxLayerCount)
				return;
			if (limit.MaxCount != -1 && GetCount(state.GlobalCount, limit) >= limit.MaxCount)
				return;
			var types = TypesOf(limit);
			if (types.Count > 0) options.Add((types, limit));
		}

		// Required-per-layer -> required-global -> any remaining -> casing fallback.
		foreach (var limit in predicate.Limited)
			if (limit.MinLayerCount > 0 && GetCount(state.LayerCount, limit) < limit.MinLayerCount)
				AddLimited(limit);
		foreach (var limit in predicate.Limited)
			if (limit.MinCount > 0 && GetCount(state.GlobalCount, limit) < limit.MinCount)
				AddLimited(limit);
		foreach (var limit in predicate.Limited)
			AddLimited(limit);

		foreach (var common in predicate.Common)
		{
			var types = TypesOf(common);
			if (types.Count > 0) options.Add((types, null));
		}

		return options;
	}

	// = upstream's `info.getBlockState().getBlock() != Blocks.AIR`.
	private static List<int> TypesOf(SimplePredicate sp)
	{
		var result = new List<int>();
		var items = sp.Candidates?.Invoke();
		if (items is null) return result;
		foreach (var it in items)
			if (it is not null && !it.IsAir)
				result.Add(it.type);
		return result;
	}

	// = upstream getMatchStackWithHandler. Slots 0..49 (main + hotbar);
	// createTile == -1 is non-placeable.
	private static int FirstInventorySlot(Player player, List<int> candidateTypes)
	{
		for (int s = 0; s < 50; s++)
		{
			var it = player.inventory[s];
			if (it is null || it.IsAir || it.stack <= 0) continue;
			if (it.createTile == -1) continue;
			if (candidateTypes.Contains(it.type)) return s;
		}
		return -1;
	}

	// Place a 2x2 with anchor at (tileX, tileY). Style2x2 + Origin(1,1) -> pass
	// the bottom-right cell to WorldGen.PlaceObject so the anchor lands at
	// (tileX, tileY) frame (0,0). All 4 cells must be air (no terrain-clearing).
	private static bool TryPlaceCellTile(int tileX, int tileY, ushort tileType)
	{
		if (tileX < 0 || tileY < 0 || tileX + 1 >= Main.maxTilesX || tileY + 1 >= Main.maxTilesY)
			return false;

		for (int dx = 0; dx < 2; dx++)
		for (int dy = 0; dy < 2; dy++)
			if (Main.tile[tileX + dx, tileY + dy].HasTile)
				return false;

		WorldGen.PlaceObject(tileX + 1, tileY + 1, tileType, mute: true, style: 0);

		Tile anchor = Main.tile[tileX, tileY];
		if (!anchor.HasTile || anchor.TileType != tileType)
			return false;

		bool mpClient = Main.netMode == NetmodeID.MultiplayerClient;

		// Sync BEFORE the entity request so the server has the tile when
		// MachinePlacedPacket.PlaceEntity runs (mirrors PlaceInWorld ordering).
		if (mpClient)
			NetMessage.SendTileSquare(Main.myPlayer, tileX, tileY, 2, 2);

		if (TileLoader.GetTile(tileType) is IMetaMachineTile machineTile)
		{
			if (mpClient)
				MachinePlacedPacket.SendRequest(tileX, tileY, tileType, null);
			else
				machineTile.PlaceEntity(tileX, tileY);
		}

		return true;
	}

	// Owning client decrements locally; MP also notifies server via SyncEquipment.
	private static void ConsumeOne(Player player, int slot)
	{
		Item it = player.inventory[slot];
		it.stack--;
		if (it.stack <= 0) it.TurnToAir();
		if (Main.netMode == NetmodeID.MultiplayerClient)
			NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, slot, it.prefix);
	}

	private static int GetCount(Dictionary<SimplePredicate, int> map, SimplePredicate key)
		=> map.TryGetValue(key, out var v) ? v : 0;
}
