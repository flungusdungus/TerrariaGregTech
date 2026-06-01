#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe.Category;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Recipe;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.recipe.GTRecipe.
// DO NOT modify behavior; mirror upstream changes only.
//
// One recipe instance. Inputs / outputs / per-tick inputs / per-tick outputs
// are stored as typed-content maps keyed by RecipeCapability - one list of
// Content entries per capability (one list of item-stacks, one of fluid-
// stacks, one with the EU EnergyStack, ...). RecipeLogic walks these maps via
// `getInputContents(EURecipeCapability.CAP)` etc. and dispatches to the
// matching IRecipeHandler trait on the machine.
//
// Documented adaptations:
//   - ResourceLocation id -> string Id (matches our IO/path conventions).
//   - CompoundTag -> TagCompound (tML).
//   - Map<RecipeCapability<?>, ...> keys typed as `object` (non-generic
//     wildcard). RecipeCapability is the runtime cast token.
//   - implements net.minecraft.world.item.crafting.Recipe<Container> dropped
//     - upstream's matches/assemble/canCraftInDimensions/getResultItem all
//     return empty/false (Recipe<Container> is just an interface MC requires
//     for items that show in the vanilla recipe book; gtceu recipes never
//     do).
//   - getSerializer / getType return MC Recipe metadata - dropped for the
//     same reason.
//   - ingredientActions (KubeJS hook) -> typed as IReadOnlyList<object>; we
//     don't have KubeJS but the slot is preserved for parity.
//   - Lazy EUt computation uses System.Lazy<T>.
public sealed class GTRecipe : IEquatable<GTRecipe>
{
	public readonly GTRecipeType RecipeType;
	public string Id { get; set; }

	public readonly Dictionary<object, List<Content.Content>> Inputs;
	public readonly Dictionary<object, List<Content.Content>> Outputs;
	public readonly Dictionary<object, List<Content.Content>> TickInputs;
	public readonly Dictionary<object, List<Content.Content>> TickOutputs;

	public readonly Dictionary<object, ChanceLogic> InputChanceLogics;
	public readonly Dictionary<object, ChanceLogic> OutputChanceLogics;
	public readonly Dictionary<object, ChanceLogic> TickInputChanceLogics;
	public readonly Dictionary<object, ChanceLogic> TickOutputChanceLogics;

	public readonly List<RecipeCondition> Conditions;
	// For KubeJS - actual type is List<IngredientAction>. Kept as
	// IReadOnlyList<object> to not crash without KubeJS (verbatim from
	// upstream's `List<?> ingredientActions` comment).
	public readonly IReadOnlyList<object> IngredientActions;
	public TagCompound Data;
	public int Duration;
	public int Parallels = 1;
	public int SubtickParallels = 1;
	public int BatchParallels = 1;
	public int OcLevel = 0;
	public readonly GTRecipeCategory RecipeCategory;
	// Raw category id from the bundle JSON (e.g. "gtceu:macerator_recycling").
	// Upstream's category registry isn't ported - RecipeCategory falls back to
	// the recipe type's default - but the recipe browser's "hide obvious"
	// filter needs to distinguish recycling categories from primary ones,
	// so the serializer stamps the raw string here after construction.
	public string? CategoryId;

	// Verbatim port of upstream's lazy `@Getter(lazy=true) inputEUt /
	// outputEUt` fields. Recomputed only when first accessed.
	private readonly Lazy<EnergyStack> _inputEUt;
	private readonly Lazy<EnergyStack> _outputEUt;
	public EnergyStack InputEUt  => _inputEUt.Value;
	public EnergyStack OutputEUt => _outputEUt.Value;

	public int GroupColor = -1;

	// Primary constructor - verbatim port of upstream's 14-arg form.
	public GTRecipe(
		GTRecipeType recipeType,
		string? id,
		Dictionary<object, List<Content.Content>> inputs,
		Dictionary<object, List<Content.Content>> outputs,
		Dictionary<object, List<Content.Content>> tickInputs,
		Dictionary<object, List<Content.Content>> tickOutputs,
		Dictionary<object, ChanceLogic> inputChanceLogics,
		Dictionary<object, ChanceLogic> outputChanceLogics,
		Dictionary<object, ChanceLogic> tickInputChanceLogics,
		Dictionary<object, ChanceLogic> tickOutputChanceLogics,
		List<RecipeCondition> conditions,
		IReadOnlyList<object> ingredientActions,
		TagCompound data,
		int duration,
		GTRecipeCategory recipeCategory,
		int groupColor)
	{
		RecipeType = recipeType;
		Id = id ?? string.Empty;

		Inputs = inputs;
		Outputs = outputs;
		TickInputs = tickInputs;
		TickOutputs = tickOutputs;

		InputChanceLogics = inputChanceLogics;
		OutputChanceLogics = outputChanceLogics;
		TickInputChanceLogics = tickInputChanceLogics;
		TickOutputChanceLogics = tickOutputChanceLogics;

		Conditions = conditions;
		IngredientActions = ingredientActions;
		Data = data;
		Duration = duration;
		// Verbatim port of upstream's fallback to recipeType.category when
		// passed GTRecipeCategory.DEFAULT.
		RecipeCategory = !recipeCategory.Equals(GTRecipeCategory.DEFAULT)
			? recipeCategory
			: recipeType.GetCategory();
		GroupColor = groupColor;

		_inputEUt  = new Lazy<EnergyStack>(() => CalculateEUt(TickInputs));
		_outputEUt = new Lazy<EnergyStack>(() => CalculateEUt(TickOutputs));
	}

	// Convenience overload (verbatim port of upstream's 13-arg constructor
	// that omits id).
	public GTRecipe(
		GTRecipeType recipeType,
		Dictionary<object, List<Content.Content>> inputs,
		Dictionary<object, List<Content.Content>> outputs,
		Dictionary<object, List<Content.Content>> tickInputs,
		Dictionary<object, List<Content.Content>> tickOutputs,
		Dictionary<object, ChanceLogic> inputChanceLogics,
		Dictionary<object, ChanceLogic> outputChanceLogics,
		Dictionary<object, ChanceLogic> tickInputChanceLogics,
		Dictionary<object, ChanceLogic> tickOutputChanceLogics,
		List<RecipeCondition> conditions,
		IReadOnlyList<object> ingredientActions,
		TagCompound data,
		int duration,
		GTRecipeCategory recipeCategory,
		int groupColor)
		: this(recipeType, null, inputs, outputs, tickInputs, tickOutputs,
			inputChanceLogics, outputChanceLogics, tickInputChanceLogics, tickOutputChanceLogics,
			conditions, ingredientActions, data, duration, recipeCategory, groupColor) { }

	// === Copy =================================================================
	// Verbatim port of upstream's 3 copy overloads.

	public GTRecipe Copy() => Copy(ContentModifier.IDENTITY, false);

	public GTRecipe Copy(ContentModifier modifier) => Copy(modifier, true);

	public GTRecipe Copy(ContentModifier modifier, bool modifyDuration)
	{
		var copied = new GTRecipe(RecipeType, Id,
			modifier.ApplyContents(Inputs), modifier.ApplyContents(Outputs),
			modifier.ApplyContents(TickInputs), modifier.ApplyContents(TickOutputs),
			new Dictionary<object, ChanceLogic>(InputChanceLogics),
			new Dictionary<object, ChanceLogic>(OutputChanceLogics),
			new Dictionary<object, ChanceLogic>(TickInputChanceLogics),
			new Dictionary<object, ChanceLogic>(TickOutputChanceLogics),
			new List<RecipeCondition>(Conditions),
			new List<object>(IngredientActions),
			Data,
			Duration,
			RecipeCategory,
			GroupColor);
		if (modifyDuration)
			copied.Duration = modifier.Apply(Duration);
		copied.OcLevel = OcLevel;
		copied.Parallels = Parallels;
		copied.BatchParallels = BatchParallels;
		copied.SubtickParallels = SubtickParallels;
		return copied;
	}

	// === Content accessors (verbatim) ========================================

	public IReadOnlyList<Content.Content> GetInputContents(object capability) =>
		Inputs.TryGetValue(capability, out var list) ? list : System.Array.Empty<Content.Content>();

	public IReadOnlyList<Content.Content> GetOutputContents(object capability) =>
		Outputs.TryGetValue(capability, out var list) ? list : System.Array.Empty<Content.Content>();

	public IReadOnlyList<Content.Content> GetTickInputContents(object capability) =>
		TickInputs.TryGetValue(capability, out var list) ? list : System.Array.Empty<Content.Content>();

	public IReadOnlyList<Content.Content> GetTickOutputContents(object capability) =>
		TickOutputs.TryGetValue(capability, out var list) ? list : System.Array.Empty<Content.Content>();

	public bool HasTick() => TickInputs.Count > 0 || TickOutputs.Count > 0;

	// Verbatim port - defaults to OR.
	public ChanceLogic GetChanceLogicForCapability(object cap, IO io, bool isTick)
	{
		if (io == IO.OUT)
			return isTick
				? (TickOutputChanceLogics.TryGetValue(cap, out var t) ? t : ChanceLogic.OR)
				: (OutputChanceLogics.TryGetValue(cap, out var o) ? o : ChanceLogic.OR);
		if (io == IO.IN)
			return isTick
				? (TickInputChanceLogics.TryGetValue(cap, out var ti) ? ti : ChanceLogic.OR)
				: (InputChanceLogics.TryGetValue(cap, out var i) ? i : ChanceLogic.OR);
		return ChanceLogic.OR;
	}

	// Verbatim port. EnergyStack values are summed across all EU contents
	// in the given content map (typically tickInputs or tickOutputs).
	private static EnergyStack CalculateEUt(Dictionary<object, List<Content.Content>> contents)
	{
		if (!contents.TryGetValue(EURecipeCapability.CAP, out var euList) || euList is null)
			return EnergyStack.EMPTY;
		long v = 0, a = 0;
		foreach (var entry in euList)
		{
			// Upstream casts via EURecipeCapability.CAP.of(content.content);
			// our payload IS already an EnergyStack since EURecipeCapability.
			// CopyInner returns it as-is.
			if (entry.Payload is EnergyStack es)
			{
				v += es.Voltage;
				a += es.Amperage;
			}
		}
		return a == 0 ? EnergyStack.EMPTY : new EnergyStack(v, a);
	}

	public int GetTotalRuns() => Parallels * SubtickParallels * BatchParallels;

	// Verbatim port - id-based equality. Upstream comment: "Technically should
	// account for overflow but realistically not an issue. Just check id as
	// there *should* only ever be 1 instance of a recipe with this id. If this
	// doesn't work, fix."
	public bool Equals(GTRecipe? other) => other is not null && Id == other.Id;
	public override bool Equals(object? obj) => obj is GTRecipe r && Equals(r);
	public override int GetHashCode() => Id.GetHashCode();
	public override string ToString() => Id;
}
