#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Primitive;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Primitive Pump multiblock GUI. No recipe loop, no item slots - only the
// water output is interesting. The fluid surface lives on the bound PumpHatch
// part, not on the controller, so this UI shows live status + production
// stats rather than a tank widget. R-click the pump_hatch directly to bucket
// out the water.
public static class PrimitivePumpLayout
{
	public static MachineUILayout Build(PrimitivePumpMachine m) => new()
	{
		Width  = 220,
		Height = 100,
		Title  = m.DisplayName,

		Widgets =
		{
			// Live status - surface the matcher's error info ("Need 1
			// pump_hatch", "wrong frame material", etc.) when unformed; once
			// the pattern matches, fall through to the production summary.
			new DynamicLabelWidgetSpec(X: 12, Y: 28,
				Getter: () =>
				{
					if (!m.IsFormed) return TerrariaCompat.Machine.RecipeStatusText.StatusLineForMulti(m, rl: null);
					int prod = m.GetFluidProduction();
					if (prod < 0) return "No water in this biome.";
					if (prod == 0) return "Biome scan pending...";
					return $"Pumping {prod} mB water / second";
				}, Scale: 0.85f),

			new DynamicLabelWidgetSpec(X: 12, Y: 50,
				Getter: () => Terraria.Main.raining ? "Boosted by rain (+50%)" : "", Scale: 0.7f),

			new DynamicLabelWidgetSpec(X: 12, Y: 70,
				Getter: () => "Right-click the pump hatch to draw water.", Scale: 0.7f),
		},
	};
}
