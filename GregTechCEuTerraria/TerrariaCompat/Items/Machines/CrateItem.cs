#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Machines;

// Non-tiered (id = `<material>_crate`); tooltip shows slot count, not voltage.
public class CrateItem : TieredMachineItem
{
	public CrateItem() { }
	public CrateItem(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	// Crates don't fit MachineRenderer's casing+overlay scheme - dedicated
	// composite for both icon and tile.
	public override void WarmUpTexture()
	{
		CrateRenderer.EnsureItemTexture(Item.type, _def?.MaterialId);
		if (Mod.TryFind<ModTile>(Name, out var t))
			CrateRenderer.EnsureTileTexture(t.Type, _def?.MaterialId);
	}

	protected override void AppendTierTooltip(List<TooltipLine> tooltips)
	{
		tooltips.Add(new TooltipLine(Mod, "CrateCapacity",
			$"Storage: {_def?.Capacity ?? 0} slots"));
	}
}
