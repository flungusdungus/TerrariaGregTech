#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Pattern;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Cursor-hover tooltip builder for unformed multi previews. Walks loaded
// controllers, finds the one whose preview covers (tileX, tileY), composes
// "Expected / Placed" lines. Shared by world hover + future cell-info popups.
public static class MultiblockPreviewHover
{
	// Ownership: a FORMED multi owns its footprint (no neighbor preview leaks
	// onto its tiles); the first UNFORMED multi whose preview maps this tile
	// claims the tooltip. No controller = no preview (entity removal in
	// KillMultiTile is the sole authority).
	public static bool TryFind(int tileX, int tileY,
		out MultiblockControllerMachine controller,
		out char ch,
		out TraceabilityPredicate predicate)
	{
		controller = null!;
		ch = ' ';
		predicate = null!;

		if (IsOwnedByFormedMulti(tileX, tileY)) return false;

		PruneStaleControllers();
		foreach (var (_, te) in TileEntity.ByPosition)
		{
			if (te is not MultiblockControllerMachine c) continue;
			if (c.IsFormed) continue;
			if (!IsControllerTileAlive(c)) continue;
			if (!c.TryGetPreviewCell(tileX, tileY, out ch, out predicate)) continue;
			controller = c;
			return true;
		}
		return false;
	}

	// False if the entity outlived its anchor tile (lava / explosion / world-edit
	// that didn't fire KillMultiTile). Stale entities must not paint previews.
	internal static bool IsControllerTileAlive(MultiblockControllerMachine c)
	{
		var p = c.Position;
		if (p.X < 0 || p.X >= Terraria.Main.maxTilesX) return false;
		if (p.Y < 0 || p.Y >= Terraria.Main.maxTilesY) return false;
		return c.IsTileValidForEntity(p.X, p.Y);
	}

	// Self-heal orphaned entities on next hover (server-only). Without this an
	// orphan paints tooltips on empty space for the world's lifetime.
	private static readonly System.Collections.Generic.List<TileEntity> _stale = new();
	private static void PruneStaleControllers()
	{
		if (Terraria.Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;
		foreach (var (_, te) in TileEntity.ByPosition)
		{
			if (te is MultiblockControllerMachine c && !IsControllerTileAlive(c))
				_stale.Add(te);
		}
		if (_stale.Count == 0) return;
		foreach (var te in _stale)
		{
			if (te is MultiblockControllerMachine c) c.OnKill();
			TileEntity.ByID.Remove(te.ID);
			TileEntity.ByPosition.Remove(te.Position);
		}
		_stale.Clear();
	}

	// Renderer uses GatherFormedFootprintsOverlapping instead - this is the
	// per-hover variant (cheap at one hit per frame).
	public static bool IsOwnedByFormedMulti(int tileX, int tileY)
	{
		foreach (var (_, te) in TileEntity.ByPosition)
		{
			if (te is MultiblockControllerMachine c && c.IsFormed
			    && IsControllerTileAlive(c)
			    && IsInsideFootprint(c, tileX, tileY))
				return true;
		}
		return false;
	}

	// One walk per Draw -> cheap rect-contains checks per cell. Without this the
	// preview hits O(N x cells) per frame when a big formed multi is on-screen.
	public static void GatherFormedFootprintsOverlapping(
		Microsoft.Xna.Framework.Rectangle bounds,
		System.Collections.Generic.List<Microsoft.Xna.Framework.Rectangle> dst)
	{
		foreach (var (_, te) in TileEntity.ByPosition)
		{
			if (te is not MultiblockControllerMachine c || !c.IsFormed) continue;
			if (!IsControllerTileAlive(c)) continue;
			var pattern = c.GetPattern();
			if (pattern is null) continue;
			var preview = pattern.GetPreviewPattern();
			int ox = c.Position.X - preview.ControllerCol * 2;
			int oy = c.Position.Y - preview.ControllerRow * 2;
			var rect = new Microsoft.Xna.Framework.Rectangle(ox, oy, preview.Width * 2, preview.Height * 2);
			if (rect.Intersects(bounds))
				dst.Add(rect);
		}
	}

	// Same math as TryGetPreviewCell, bounds-only.
	private static bool IsInsideFootprint(MultiblockControllerMachine c, int tileX, int tileY)
	{
		var pattern = c.GetPattern();
		if (pattern is null) return false;
		var preview = pattern.GetPreviewPattern();
		int originX = c.Position.X - preview.ControllerCol * 2;
		int originY = c.Position.Y - preview.ControllerRow * 2;
		return tileX >= originX && tileX < originX + preview.Width * 2
		    && tileY >= originY && tileY < originY + preview.Height * 2;
	}

	// `tileX/tileY` needed for the 2x2-alignment match check, not just tile type.
	public static void AppendTooltip(List<string> lines,
		MultiblockControllerMachine controller,
		TraceabilityPredicate predicate,
		int tileX, int tileY)
	{
		var hovered = Terraria.Main.tile[tileX, tileY];
		ushort currentTileType = hovered.HasTile ? hovered.TileType : (ushort)0;
		lines.Add($"[c/AAEEFF:{controller.DisplayName} - Multiblock Slot]");

		// Same description/builder surface as placed-tile + item hover.
		MachineTooltipLookup.AppendDescriptionAndBuilder(lines, controller.MachineKey, controller.MachineId, controller.Definition);

		if (predicate.IsAir())
		{
			lines.Add("Expected: [c/AAAAAA:air]");
		}
		else if (predicate.IsAny())
		{
			lines.Add("Expected: [c/AAAAAA:any block]");
		}
		else if (predicate.IsController)
		{
			lines.Add("Expected: [c/AAEEFF:controller (this machine)]");
		}
		else
		{
			// Tier-collapsed - 15 tiers of "Input Bus" -> one row.
			var candidates = GatherCandidateNames(predicate);
			if (candidates.Count == 0)
				lines.Add("Expected: [c/AAAAAA:(no candidates registered)]");
			else
			{
				lines.Add("Expected:");
				foreach (var name in candidates)
					lines.Add("  " + name);
			}
		}

		if (currentTileType == 0)
		{
			lines.Add("Placed: [c/AAAAAA:empty]");
		}
		else
		{
			string placedName = TileTypeName(currentTileType);
			// Walk hover coords back to the 2x2 anchor - the matcher's alignment
			// check only passes at TileFrame (0,0). Tooltip-only; renderer
			// already hits PredicateMatchesTileAt at the cell-grid anchor.
			int matchX = tileX, matchY = tileY;
			var data = Terraria.ObjectData.TileObjectData.GetTileData(hovered);
			if (data != null && (data.Width > 1 || data.Height > 1))
			{
				matchX -= (hovered.TileFrameX / 18) % data.Width;
				matchY -= (hovered.TileFrameY / 18) % data.Height;
			}
			bool matches = predicate.IsAny()
				|| (predicate.IsAir() && currentTileType == 0)
				|| PredicateMatchesTileAt(predicate, matchX, matchY);
			string marker = matches ? "[c/44FF44:✓]" : "[c/FF4444:✗]";
			lines.Add($"Placed: {placedName} {marker}");
		}
	}

	// Dedup by raw item-type, then by tier-collapsed name key so a multi-tier
	// ability predicate (e.g. Abilities(IMPORT_ITEMS) over all 15 tiers) shows
	// "Input Bus" once instead of 15 rows.
	private static List<string> GatherCandidateNames(TraceabilityPredicate predicate)
	{
		var names    = new List<string>();
		var seenType = new HashSet<int>();
		var seenKey  = new HashSet<string>(System.StringComparer.Ordinal);
		Add(predicate.Common);
		Add(predicate.Limited);
		return names;

		void Add(List<SimplePredicate> bucket)
		{
			foreach (var sp in bucket)
			{
				if (sp.Candidates is null) continue;
				var items = sp.Candidates();
				if (items is null) continue;
				foreach (var item in items)
				{
					if (item is null || item.IsAir) continue;
					if (!seenType.Add(item.type)) continue;
					string full = Lang.GetItemName(item.type).Value;
					string key  = StripTierPrefix(full);
					if (!seenKey.Add(key)) continue;
					names.Add(key);
				}
			}
		}
	}

	// Strip leading short-tier token + space; pass-through if no tier prefix.
	private static string StripTierPrefix(string name)
	{
		int sp = name.IndexOf(' ');
		if (sp <= 0) return name;
		string first = name.Substring(0, sp);
		if (System.Array.IndexOf(_tierShortNames, first) < 0) return name;
		return name.Substring(sp + 1);
	}

	private static readonly string[] _tierShortNames =
	{
		"ULV", "LV", "MV", "HV", "EV", "IV", "LuV", "ZPM",
		"UV",  "UHV","UEV","UIV","UXV","OpV","MAX",
	};

	// Drive the real SimplePredicate.Predicate functions over a transient
	// MultiblockState - keeps preview / tooltip OK in sync with the actual
	// matcher (BlockPattern.CheckPatternAt -> PredicateBlocks.Test) for free.
	// Side-effecting predicates land on a throwaway MatchContext.
	public static bool PredicateMatchesTileAt(TraceabilityPredicate predicate, int tileX, int tileY)
	{
		if (tileX < 0 || tileX >= Terraria.Main.maxTilesX) return false;
		if (tileY < 0 || tileY >= Terraria.Main.maxTilesY) return false;

		var state = new MultiblockState(tileX, tileY);
		state.Clean();
		if (!state.Update(tileX, tileY, predicate)) return false;
		return RunBucket(predicate.Common, state) || RunBucket(predicate.Limited, state);

		static bool RunBucket(List<SimplePredicate> bucket, MultiblockState state)
		{
			foreach (var sp in bucket)
			{
				if (sp.Predicate is null) continue;
				try { if (sp.Predicate(state)) return true; }
				catch { /* defensive - preview should never throw on the UI thread */ }
			}
			return false;
		}
	}

	// Item type of the block currently placed at the cell anchor (tileX, tileY),
	// via the same tile->sibling-item convention as TileTypeName. 0 = empty /
	// unresolved. Used by the ghost renderer to skip the red swap-hint on a cell
	// that already holds the missing part (its item type in the swap set).
	public static int PlacedSiblingItemType(int tileX, int tileY)
	{
		if (tileX < 0 || tileX >= Terraria.Main.maxTilesX) return 0;
		if (tileY < 0 || tileY >= Terraria.Main.maxTilesY) return 0;
		var t = Terraria.Main.tile[tileX, tileY];
		if (!t.HasTile) return 0;
		var modTile = TileLoader.GetTile(t.TileType);
		if (modTile != null && modTile.Mod.TryFind<ModItem>(modTile.Name, out var mi))
			return mi.Type;
		return 0;
	}

	private static string TileTypeName(ushort tileType)
	{
		var modTile = TileLoader.GetTile(tileType);
		if (modTile != null)
		{
			// Sibling item name (PredicateBlocks.Candidates convention).
			if (modTile.Mod.TryFind<ModItem>(modTile.Name, out var modItem))
				return Lang.GetItemName(modItem.Type).Value;
			return modTile.Name;
		}
		string vanillaName = Lang.GetMapObjectName(Terraria.Map.MapHelper.TileToLookup(tileType, 0));
		return string.IsNullOrEmpty(vanillaName) ? $"Tile #{tileType}" : vanillaName;
	}
}
