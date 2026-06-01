#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Ingredient;

namespace GregTechCEuTerraria.Api.Capability.Recipe;

// LOCKED - port of
// com.gregtechceu.gtceu.api.capability.recipe.EURecipeCapability.
// DO NOT modify behavior; mirror upstream changes only.
//
// The energy recipe capability - RecipeLogic groups all EnergyStack
// ingredients/outputs under this token and dispatches the group to whichever
// handler returns this from `getCapability()`. There's exactly one
// canonical instance: CAP.
//
// Documented deferrals (lands with the RecipeLogic port):
//   - Recipe-side helpers `makeRequestEnergy(long)`, `makeProvideEnergy(long)`
//     dropped - recipe construction goes through our JSON loader.
//   - serializer / content codec dropped - System.Text.Json handles the wire
//     shape via GTRecipeSerializer.
//   - Widget UI helpers dropped - bound in our MachineUILayout.
//
// EnergyStack is an immutable struct in our port; copyInner returns the
// value directly (no defensive copy needed).
public sealed class EURecipeCapability : RecipeCapability<EnergyStack>
{
	public static readonly EURecipeCapability CAP = new();

	private EURecipeCapability() : base("eu") { }

	public override EnergyStack CopyInner(EnergyStack content) => content;

	// Verbatim port of copyWithModifier - multiplies the EnergyStack's voltage
	// by the ContentModifier. Used by ModifierFunction.FunctionBuilder when an
	// eutMultiplier is set (overclock / parallel rescaling of EU/t).
	public override EnergyStack CopyWithModifier(EnergyStack content, ContentModifier modifier) =>
		content.WithVoltage(modifier.Apply(content.Voltage));

	// Verbatim port of makeEUContent - wraps an EnergyStack in a singleton
	// Content list at full (guaranteed) chance.
	public static List<Content> MakeEUContent(EnergyStack eu) =>
		new() { new Content(eu, ChanceLogic.GetMaxChancedValue(), ChanceLogic.GetMaxChancedValue(), 0) };

	// Verbatim port of putEUContent - replaces the EU content entry in a
	// recipe content map.
	public static void PutEUContent(Dictionary<object, List<Content>> contents, EnergyStack eu) =>
		contents[CAP] = MakeEUContent(eu);
}
