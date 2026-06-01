#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Routes loot-row hover metadata (source NPC / shop / shimmer + chance /
// condition text) into vanilla's tooltip pipeline so it renders inline with
// the hovered item's name + stats + lore + sell-value lines. UILootList
// stamps the source via PushHover(); this GlobalItem reads + clears the
// stash on the very next ModifyTooltips invocation so it never bleeds onto
// unrelated item hovers (inventory slot tooltips, drop-on-ground tooltips).
public sealed class LootTooltipGlobal : GlobalItem
{
	private static string? _pendingSource;
	private static string? _pendingDetail;

	public static void PushHover(string sourceLabel, string detail)
	{
		_pendingSource = sourceLabel;
		_pendingDetail = detail;
	}

	public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
	{
		if (_pendingSource is null) return;
		string source = _pendingSource;
		string detail = _pendingDetail ?? string.Empty;
		// Single-shot: clear immediately so any subsequent ModifyTooltips
		// pass (a different item hover later in the same frame) doesn't
		// see stale loot text.
		_pendingSource = null;
		_pendingDetail = null;

		tooltips.Add(new TooltipLine(Mod, "GTLootSource", $"From: {source}")
		{
			OverrideColor = new Color(255, 220, 140),
		});
		if (!string.IsNullOrWhiteSpace(detail))
		{
			tooltips.Add(new TooltipLine(Mod, "GTLootDetail", detail)
			{
				OverrideColor = new Color(170, 180, 200),
			});
		}
	}
}
