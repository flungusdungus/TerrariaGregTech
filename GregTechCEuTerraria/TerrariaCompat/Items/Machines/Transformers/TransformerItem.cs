#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Machines.Transformers;

// One class for all four baseAmp variants x all tiers. Bakes the DOWN-state
// 2-face composite (no block/machines/transformer_* texture exists upstream).
public class TransformerItem : TieredMachineItem
{
	public TransformerItem() { }
	public TransformerItem(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	// 1 / 2 / 4 / 16 - selects the energy overlays baked onto the icon.
	private int BaseAmp => _def!.BaseAmp;

	public override void WarmUpTexture() =>
		MachineRenderer.EnsureTransformerItem(Item.type, _tier, BaseAmp);

	protected override void AppendTierTooltip(List<TooltipLine> tooltips)
	{
		base.AppendTierTooltip(tooltips);
		long v = VoltageTiers.Voltage(_tier);
		tooltips.Add(new TooltipLine(Mod, "TransformerFaces",
			$"[c/CC55FF:Top] row: high voltage ({v * 4:N0} V)  *  [c/CC55FF:Bottom] row: low voltage ({v:N0} V)"));
		tooltips.Add(new TooltipLine(Mod, "TransformerTool",
			"Default: [c/CC55FF:step down] (top IN -> bottom OUT)  *  Screwdriver-RMB to flip to step up"));
	}
}
