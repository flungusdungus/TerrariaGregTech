#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// Aluminium large fluid cell - 32 000 mB. Mirrors upstream
// FLUID_CELL_LARGE_ALUMINIUM (createFluidCell(Aluminium, 32, 4, 64)).
public sealed class AluminiumFluidCell : FluidCellItem
{
	protected override string SnakeName => "aluminium_fluid_cell";
	protected override string Label    => "Aluminium Fluid Cell";
	public override int Capacity       => 32_000;
}
