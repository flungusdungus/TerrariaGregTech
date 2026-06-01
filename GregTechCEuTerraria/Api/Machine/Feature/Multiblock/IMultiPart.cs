#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.Api.Machine.Feature.Multiblock;

// Port of com.gregtechceu.gtceu.api.machine.feature.multiblock.IMultiPart.
//
// Marker contract for a multiblock part machine - hatches, buses, mufflers,
// maintenance / parallel / muffler / data hatches, etc. A part is bound to
// one or more controllers (`canShared` allows sharing); the controller scans
// for parts during a pattern check and aggregates their handlers into its
// RecipeLogic via `GetRecipeHandlers()`.
//
// Documented adaptations:
//   - `BlockPos controllerPos` -> `(int X, int Y)` tile-space anchor of the
//     controller's 2x2 block.
//   - `Component` -> `string` (no Mojang text system).
//   - `BlockState getFormedAppearance(..., Direction side)` rendering hook
//     DROPPED. In Terraria a part's "looks like the controller's casing when
//     formed" is solved via the renderer reading the part's IsFormed flag -
//     no per-face appearance contract is needed in 2D.
//   - `TooltipsPanel` (the fancy-tooltip side-panel) attach hook DROPPED;
//     part tooltips surface via the controller's existing fancy-tooltip path.
//   - Upstream extends `IMachineFeature, IFancyUIMachine`. We drop both
//     parents - `IMachineFeature` is a one-default-method marker we don't
//     have yet (callers cast `this` to `MetaMachine` directly), and the
//     fancy-UI parent's surface is unused at this layer.
public interface IMultiPart
{
	// Cast-to-MetaMachine accessor. Upstream's IMachineFeature default - we
	// don't have IMachineFeature, so the method lands here directly. Every
	// IMultiPart impl is also a MetaMachine.
	MetaMachine Self() => (MetaMachine)this;

	// If this part can belong to multiple controllers at once. Default true -
	// e.g. an item-input bus shared between two adjacent multis.
	bool CanShared() => true;

	// Does this part belong to a controller whose anchor is at the given tile?
	bool HasController(int controllerPosX, int controllerPosY);

	// True iff this part belongs to at least one formed multiblock.
	bool IsFormed();

	// Every controller this part belongs to.
	IReadOnlyCollection<MultiblockControllerMachine> GetControllers();

	void RemovedFromController(MultiblockControllerMachine controller);
	void AddedToController(MultiblockControllerMachine controller);

	// The bundled recipe handlers this part exposes to its controller's
	// RecipeLogic. Usually one entry per part (the part's own IO traits).
	List<RecipeHandlerList> GetRecipeHandlers();

	// If true, the part's render switches to the controller's "formed"
	// appearance - drawn as part of the multi rather than its own item icon.
	bool ReplacePartModelWhenFormed() => true;

	// Lifecycle hooks called from the controller's `RecipeLogic.HandleRecipeWorking`
	// / `setupRecipe` / `onRecipeFinish`. Returning false from BeforeWorking
	// vetoes the recipe start; returning false from OnWorking / OnWaiting
	// stops the cycle.
	bool OnWorking   (IWorkableMultiController controller) => true;
	bool OnWaiting   (IWorkableMultiController controller) => true;
	bool OnPaused    (IWorkableMultiController controller) => true;
	bool AfterWorking(IWorkableMultiController controller) => true;
	bool BeforeWorking(IWorkableMultiController controller) => true;

	// Modify the recipe before it runs (e.g. parallel hatches multiply
	// throughput, fluid regulator adjusts amounts). Return null to veto the
	// recipe entirely - RotorHolder rejects when no rotor is installed, for
	// instance. Default identity.
	GTRecipe? ModifyRecipe(GTRecipe recipe) => recipe;

	// Add part-side lines to the multi's status tooltip (e.g. battery rack
	// shows total stored EU). Default empty.
	void AddMultiText(List<string> textList) { }
}
