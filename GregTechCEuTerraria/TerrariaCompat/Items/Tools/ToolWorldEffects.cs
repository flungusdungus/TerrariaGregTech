#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// World-side identity-tool behaviour: soft-digger tile filter (CanKillTile),
// saw -> rubber wood + mortar -> silt/sand (CanDrop), hoe -> tier-scaled extra
// seeds (Drop). Drop runs server-side in MP (server-authoritative drops); the
// breaking player is resolved via the nearest tool-holder (a stand-in for
// vanilla's non-public GetPlayerForTile).
public sealed class ToolWorldEffects : GlobalTile
{
	private static int _rubberWood = -1;
	private static int RubberWood => _rubberWood >= 0
		? _rubberWood
		: (_rubberWood = IngredientResolverImpl.Instance.ResolveItemType("gtceu:rubber_wood"));

	private static readonly HashSet<int> SoftTiles = new()
	{
		TileID.Dirt, TileID.Grass, TileID.CorruptGrass, TileID.CrimsonGrass,
		TileID.HallowedGrass, TileID.Mud, TileID.JungleGrass, TileID.MushroomGrass,
		TileID.Sand, TileID.Ebonsand, TileID.Crimsand, TileID.Pearlsand,
		TileID.HardenedSand, TileID.CorruptHardenedSand, TileID.CrimsonHardenedSand,
		TileID.HallowHardenedSand, TileID.Silt, TileID.Slush, TileID.SnowBlock,
		TileID.ClayBlock, TileID.Cloud, TileID.RainCloud, TileID.Ash,
	};

	public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged)
	{
		if (Main.netMode == NetmodeID.Server) return true;       // client-side gate only
		var p = Main.LocalPlayer;
		if (p?.HeldItem?.ModItem is ToolItem t && t.IsSoftDigger && p.itemAnimation > 0)
			return SoftTiles.Contains(type);
		return true;
	}

	// REPLACE cases: TileLoader.Drop early-returns BEFORE Drop hooks when
	// CanDrop is false, so the substitute MUST be spawned here, not in Drop.
	public override bool CanDrop(int i, int j, int type)
	{
		var holder = NearestToolHolder(i, j);
		if (holder?.HeldItem?.ModItem is not ToolItem tool) return true;

		// Saw / buzzsaw: trees yield rubber wood instead of Wood.
		if (tool.IsSawLike && (type == TileID.Trees || type == TileID.PalmTree) && RubberWood > 0)
		{
			SpawnItem(i, j, RubberWood, 1);
			return false;
		}

		// Mortar: stone yields Silt Block, dirt yields Sand Block.
		if (tool.IsMortar && type == TileID.Stone)
		{
			SpawnItem(i, j, ItemID.SiltBlock, 1);
			return false;
		}
		if (tool.IsMortar && type == TileID.Dirt)
		{
			SpawnItem(i, j, ItemID.SandBlock, 1);
			return false;
		}

		return true;
	}

	// ADDITIVE: hoe's tier-scaled Staff-of-Regrowth bonus (vanilla's
	// staffOfRegrowthBonus flag isn't exposed to mods). Anchored so Aluminium
	// (MV/tier 2) ~ vanilla Staff's 1-5 bonus seeds + 1 extra herb.
	public override void Drop(int i, int j, int type)
	{
		if (type != TileID.MatureHerbs && type != TileID.BloomingHerbs) return;
		var holder = NearestToolHolder(i, j);
		if (holder?.HeldItem?.ModItem is not ToolItem tool || !tool.IsHoe) return;

		// Verbatim WorldGen.KillTile_GetItemDrops math.
		int num = Main.tile[i, j].TileFrameX / 18;
		int seed = num == 6 ? 2357 : 307 + num;
		int herb = num == 6 ? 2358 : 313 + num;

		// tier 2 -> up to 5 (Staff parity); tier 0 -> up to 1; tier 9 -> up to ~22.
		int seedCap = System.Math.Max(1, (int)System.Math.Round(5.0 * tool.Tier / 2.0));
		int bonusSeeds = Main.rand.Next(0, seedCap + 1);
		SpawnItem(i, j, seed, bonusSeeds);

		// Aluminium and above also get the Staff's bonus harvested herb.
		if (tool.Tier >= 2) SpawnItem(i, j, herb, 1);
	}

	private static void SpawnItem(int i, int j, int itemType, int stack)
	{
		if (itemType <= 0 || stack <= 0) return;
		if (Main.netMode == NetmodeID.MultiplayerClient) return;     // server-authoritative
		Item.NewItem(WorldGen.GetItemSource_FromTileBreak(i, j),
			i * 16, j * 16, 16, 16, itemType, stack);
	}

	// Stand-in for vanilla's non-public WorldGen.GetPlayerForTile.
	private static Player? NearestToolHolder(int i, int j)
	{
		var c = new Vector2(i * 16f + 8f, j * 16f + 8f);
		Player? best = null;
		float bestSq = float.MaxValue;
		for (int p = 0; p < Main.maxPlayers; p++)
		{
			var pl = Main.player[p];
			if (pl is null || !pl.active || pl.dead) continue;
			if (pl.HeldItem?.ModItem is not ToolItem) continue;
			float d = Vector2.DistanceSquared(pl.Center, c);
			if (d < bestSq) { bestSq = d; best = pl; }
		}
		const float maxPx = 80f * 16f;
		return best != null && bestSq <= maxPx * maxPx ? best : null;
	}
}
