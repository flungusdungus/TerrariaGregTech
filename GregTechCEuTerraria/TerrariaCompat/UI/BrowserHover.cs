#nullable enable
using System.Collections.Generic;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Recorded by UIRecipeList per frame so R/U hotkeys can read item/fluid/tag
// cells (Main.HoverItem doesn't carry fluids/tags). Frame-stamped so stale
// reads ignore.
internal static class BrowserHover
{
	private static uint _frame;

	public static int ItemType { get; private set; }
	public static string? FluidId { get; private set; }
	public static string? FluidLabel { get; private set; }
	public static string? TagLabel { get; private set; }
	public static HashSet<int>? TagItems { get; private set; }

	public static void SetItem(int itemType)
	{
		ItemType = itemType;
		FluidId = null;
		FluidLabel = null;
		TagLabel = null;
		TagItems = null;
		_frame = Main.GameUpdateCount;
	}

	public static void SetFluid(string id, string label)
	{
		FluidId = id;
		FluidLabel = label;
		ItemType = 0;
		TagLabel = null;
		TagItems = null;
		_frame = Main.GameUpdateCount;
	}

	public static void SetTag(string label, HashSet<int> items)
	{
		TagLabel = label;
		TagItems = items;
		ItemType = 0;
		FluidId = null;
		FluidLabel = null;
		_frame = Main.GameUpdateCount;
	}

	// Recorded in draw, read one update later - 2-frame fresh window.
	public static bool Fresh => Main.GameUpdateCount - _frame <= 2;
}
