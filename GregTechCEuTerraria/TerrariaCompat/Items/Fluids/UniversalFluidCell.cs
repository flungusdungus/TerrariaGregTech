#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// 1000 mB. Mirrors GTItems.FLUID_CELL_UNIVERSAL. "Universal" -> auto-returns the
// empty when a recipe consumes the filled cell; the behavioural distinction
// kicks in at machine-IO time (deferred until that step lands).
public sealed class UniversalFluidCell : FluidCellItem
{
	protected override string SnakeName => "universal_fluid_cell";
	protected override string Label    => "Universal Fluid Cell";
	public override int Capacity       => 1000;
}
