#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// Stainless steel large fluid cell - 64 000 mB. Mirrors upstream
// FLUID_CELL_LARGE_STAINLESS_STEEL (createFluidCell(StainlessSteel, 64, 6, 64)).
public sealed class StainlessSteelFluidCell : FluidCellItem
{
	protected override string SnakeName => "stainless_steel_fluid_cell";
	protected override string Label    => "Stainless Steel Fluid Cell";
	public override int Capacity       => 64_000;
}
