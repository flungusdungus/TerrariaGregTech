#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// Basic tin fluid cell - 1000 mB, 64-stack. Mirrors upstream GTItems.FLUID_CELL.
public sealed class FluidCell : FluidCellItem
{
	protected override string SnakeName => "fluid_cell";
	protected override string Label    => "Fluid Cell";
	public override int Capacity       => 1000;
}
