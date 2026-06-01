#nullable enable
using GregTechCEuTerraria.Common.Energy;
using System.Collections.Generic;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// Tints the "ItemName" tooltip line with the tier colour (upstream GTValues.VCM).
public static class TierTooltip
{
	public static void ApplyTierColor(this List<TooltipLine> tooltips, VoltageTier tier)
	{
		var color = VoltageTiers.TextColor(tier);
		foreach (var line in tooltips)
		{
			if (line.Mod == "Terraria" && line.Name == "ItemName")
			{
				line.OverrideColor = color;
				return;
			}
		}
	}
}
