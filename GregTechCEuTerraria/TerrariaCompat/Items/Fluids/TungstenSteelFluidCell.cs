#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// Tungsten steel large fluid cell - 512 000 mB. Mirrors upstream
// FLUID_CELL_LARGE_TUNGSTEN_STEEL (createFluidCell(TungstenSteel, 512, 8, 32)).
// Stack size inherits FluidCellItem.CellMaxStack (= 99).
public sealed class TungstenSteelFluidCell : FluidCellItem
{
	protected override string SnakeName => "tungsten_steel_fluid_cell";
	protected override string Label    => "Tungsten Steel Fluid Cell";
	public override int Capacity       => 512_000;
}
