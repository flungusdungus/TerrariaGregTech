#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// Titanium large fluid cell - 128 000 mB. Mirrors upstream
// FLUID_CELL_LARGE_TITANIUM (createFluidCell(Titanium, 128, 6, 64)).
public sealed class TitaniumFluidCell : FluidCellItem
{
	protected override string SnakeName => "titanium_fluid_cell";
	protected override string Label    => "Titanium Fluid Cell";
	public override int Capacity       => 128_000;
}
