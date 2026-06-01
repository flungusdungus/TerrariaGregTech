#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using GregTechCEuTerraria.Common.Recipe;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Port of ElectricBlastFurnaceMachine. Upstream uses the bare
// CoilWorkableElectricMultiblockMachine + ebfOverclock; we subclass for an
// explicit type to host FusedCasingTileType + EBF tooltips. Batch-mode
// modifier deferred.
public sealed class ElectricBlastFurnaceMachine : CoilWorkableElectricMultiblockMachine
{
	protected override string Label => "Electric Blast Furnace";

	public ElectricBlastFurnaceMachine() : base() { }

	// Mirrors upstream .recipeModifiers(GTRecipeModifiers::ebfOverclock, ...).
	public override RecipeModifier GetRecipeModifier() => GTRecipeModifiers.EBF_OVERCLOCK;
}
