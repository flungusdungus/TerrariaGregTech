#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Machines;

// Non-tiered (id = `<material>_drum`); tooltip shows fluid capacity + contents
// carried in MachinePortableData when a filled drum is broken.
public class DrumItem : TieredMachineItem
{
	public DrumItem() { }
	public DrumItem(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	// Drums don't fit MachineRenderer's casing+overlay scheme.
	public override void WarmUpTexture()
	{
		DrumRenderer.EnsureItemTexture(Item.type, _def?.MaterialId);
		if (Mod.TryFind<ModTile>(Name, out var t))
			DrumRenderer.EnsureTileTexture(t.Type, _def?.MaterialId);
	}

	protected override void AppendTierTooltip(List<TooltipLine> tooltips)
	{
		int cap = _def?.Capacity ?? 0;
		tooltips.Add(new TooltipLine(Mod, "DrumCapacity", $"Fluid capacity: {cap:N0} mB"));

		// Stored fluid stamped into MachinePortableData by GetItemDrops.
		if (Item.TryGetGlobalItem<MachinePortableData>(out var g) && g.Data is { } d
		    && d.ContainsKey("fluidId")
		    && FluidRegistry.TryGet(d.GetString("fluidId"), out var type))
			tooltips.Add(new TooltipLine(Mod, "DrumContents",
				$"Contains {d.GetInt("fluidAmount"):N0} mB of {type.DisplayName}"));
	}
}
