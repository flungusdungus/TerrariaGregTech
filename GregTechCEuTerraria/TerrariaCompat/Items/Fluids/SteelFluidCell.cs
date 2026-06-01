#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// Steel large fluid cell - 8000 mB, 64-stack. Mirrors upstream GTItems
// .FLUID_CELL_LARGE_STEEL (createFluidCell(Steel, 8, 4, 64)) - capacity is
// the buckets multiplier x 1000 mB per bucket.
public sealed class SteelFluidCell : FluidCellItem
{
	protected override string SnakeName => "steel_fluid_cell";
	protected override string Label    => "Steel Fluid Cell";
	public override int Capacity       => 8_000;
}
