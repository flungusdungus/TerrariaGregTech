#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Shared "cursor is over a world thing" tooltip surface (placed wire/pipe/...).
// Route through here instead of calling MouseText directly: a direct MouseText
// from HoldItem / PostUpdateInput runs BEFORE mouseInterface is populated, so
// the world hint leaks through any UI panel over that coord. This flush lives on
// an InterfaceLayer (draw phase, after every UpdateUI) where mouseInterface is
// accurate, so we can suppress it under a UI element.
// Single-string per frame, last-write-wins ("\n"-joined for multi-line).
public sealed class WorldHoverTooltip : ModSystem
{
	private static string? _pending;

	// Latest call before the InterfaceLayer flush wins.
	public static void Set(string text) => _pending = text;

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		// Before "Vanilla: Mouse Text" so our MouseText is what renders.
		UILayers.InsertModal(layers,
			"GregTechCEuTerraria: World Hover Tooltip",
			() =>
			{
				if (_pending is not null && !Main.LocalPlayer.mouseInterface)
				{
					Main.LocalPlayer.cursorItemIconEnabled = false;
					Main.instance.MouseText(_pending);
				}
				_pending = null;
				return true;
			});
	}
}
