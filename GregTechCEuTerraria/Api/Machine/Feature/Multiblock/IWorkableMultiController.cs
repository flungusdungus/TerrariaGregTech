#nullable enable
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.Api.Machine.Feature.Multiblock;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.feature.multiblock.IWorkableMultiController.
//
// Marker contract for the family of multiblock controllers that drive a
// `RecipeLogic` trait - combines `IRecipeLogicMachine` (the recipe state
// machine surface) with a `Self()` accessor that hands the typed
// MultiblockControllerMachine reference back. Part machines call into
// `controller.Self()` from their lifecycle hooks (onWorking / onWaiting /
// beforeWorking / afterWorking) to read the controller's state / parts /
// pattern.
//
// Note: `MultiblockControllerMachine` is still a STUB (see the class header).
// Until the 3D->2D pattern decision resolves, this interface only contributes
// the IRecipeLogicMachine plumbing and a typed handle for forward refs.
public interface IWorkableMultiController : IRecipeLogicMachine
{
	MultiblockControllerMachine Self();
}
