#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Content;

namespace GregTechCEuTerraria.Api.Recipe.Modifier;

// Port of com.gregtechceu.gtceu.api.recipe.modifier.ModifierFunction.
//
// A function that accepts a GTRecipe and returns a modified copy, or null to
// cancel the recipe. Upstream is a @FunctionalInterface with default methods;
// C# has no functional-interface sugar, so this is a sealed class wrapping a
// delegate. compose / andThen / NULL / IDENTITY / cancel / builder all match
// upstream.
//
// Documented adaptation:
//   - getFailReason returns a Component upstream; we have no Component system,
//     so FailReason is a localization-key string.
public sealed class ModifierFunction
{
	public const string DEFAULT_FAILURE = "gtceu.recipe_modifier.default_fail";

	private readonly Func<GTRecipe, GTRecipe?> _apply;
	public string FailReason { get; }

	private ModifierFunction(Func<GTRecipe, GTRecipe?> apply, string failReason = DEFAULT_FAILURE)
	{
		_apply = apply;
		FailReason = failReason;
	}

	// Use this to denote that the recipe should be cancelled.
	public static readonly ModifierFunction NULL = new(_ => null);

	// Use this to denote that the recipe doesn't get modified.
	public static readonly ModifierFunction IDENTITY = Builder().Build();

	public static ModifierFunction Cancel(string reason) => new(_ => null, reason);

	// Lambda factory - upstream's `r -> { ... return copy; }` shape. Used by
	// the multi-smelter parallel modifier (and any future modifier that needs
	// to rewrite duration / EUt outside the FunctionBuilder's vocabulary).
	public static ModifierFunction Of(Func<GTRecipe, GTRecipe?> apply) => new(apply);

	// Applies this modifier to the passed recipe. Returns a new GTRecipe with
	// modifications, or null if the recipe should be cancelled.
	public GTRecipe? Apply(GTRecipe recipe) => _apply(recipe);

	private GTRecipe? ApplySafe(GTRecipe? recipe) => recipe is null ? null : Apply(recipe);

	// Composed function of `this.apply(before.apply(recipe))`.
	public ModifierFunction Compose(ModifierFunction before) =>
		new(recipe => ApplySafe(before.Apply(recipe)));

	// Composed function of `after.apply(this.apply(recipe))`.
	public ModifierFunction AndThen(ModifierFunction after) =>
		new(recipe => after.ApplySafe(Apply(recipe)));

	public static FunctionBuilder Builder() => new();

	// Port of ModifierFunction.FunctionBuilder. Fluent builder; build()
	// produces a ModifierFunction that copies the recipe with the configured
	// content / duration / EUt / parallel / OC modifiers applied.
	public sealed class FunctionBuilder
	{
		private int _parallels = 1;
		private int _subtickParallels = 1;
		private int _batchParallels = 1;
		private int _addOCs = 0;
		private ContentModifier _eutModifier = ContentModifier.IDENTITY;
		private ContentModifier _durationModifier = ContentModifier.IDENTITY;
		private ContentModifier _inputModifier = ContentModifier.IDENTITY;
		private ContentModifier _outputModifier = ContentModifier.IDENTITY;
		private ContentModifier _tickInputModifier = ContentModifier.IDENTITY;
		private ContentModifier _tickOutputModifier = ContentModifier.IDENTITY;
		private readonly List<RecipeCondition> _addedConditions = new();

		public FunctionBuilder Parallels(int v)         { _parallels = v;         return this; }
		public FunctionBuilder SubtickParallels(int v)  { _subtickParallels = v;  return this; }
		public FunctionBuilder BatchParallels(int v)    { _batchParallels = v;    return this; }
		public FunctionBuilder AddOCs(int v)            { _addOCs = v;            return this; }
		public FunctionBuilder InputModifier(ContentModifier cm)      { _inputModifier = cm;      return this; }
		public FunctionBuilder OutputModifier(ContentModifier cm)     { _outputModifier = cm;     return this; }
		public FunctionBuilder TickInputModifier(ContentModifier cm)  { _tickInputModifier = cm;  return this; }
		public FunctionBuilder TickOutputModifier(ContentModifier cm) { _tickOutputModifier = cm; return this; }

		public FunctionBuilder Conditions(params RecipeCondition[] conditions)
		{
			_addedConditions.AddRange(conditions);
			return this;
		}

		public FunctionBuilder ModifyAllContents(ContentModifier cm)
		{
			_inputModifier = cm;
			_outputModifier = cm;
			_tickInputModifier = cm;
			_tickOutputModifier = cm;
			return this;
		}

		public FunctionBuilder EutMultiplier(double multiplier)
		{
			_eutModifier = ContentModifier.Multiplier_(multiplier);
			return this;
		}

		public FunctionBuilder DurationMultiplier(double multiplier)
		{
			_durationModifier = ContentModifier.Multiplier_(multiplier);
			return this;
		}

		public ModifierFunction Build()
		{
			if (_parallels == 0) return NULL;

			// Capture builder state by value so the closure is independent of
			// later builder mutation.
			int parallels = _parallels, subtickParallels = _subtickParallels;
			int batchParallels = _batchParallels, addOCs = _addOCs;
			ContentModifier eutMod = _eutModifier, durMod = _durationModifier;
			ContentModifier inMod = _inputModifier, outMod = _outputModifier;
			ContentModifier tickInMod = _tickInputModifier, tickOutMod = _tickOutputModifier;
			var addedConditions = new List<RecipeCondition>(_addedConditions);

			return new ModifierFunction(recipe =>
			{
				var newConditions = new List<RecipeCondition>(recipe.Conditions);
				newConditions.AddRange(addedConditions);
				var copied = new GTRecipe(recipe.RecipeType, recipe.Id,
					inMod.ApplyContents(recipe.Inputs),
					outMod.ApplyContents(recipe.Outputs),
					ApplyAllButEU(tickInMod, recipe.TickInputs),
					ApplyAllButEU(tickOutMod, recipe.TickOutputs),
					new Dictionary<object, ChanceLogic>(recipe.InputChanceLogics),
					new Dictionary<object, ChanceLogic>(recipe.OutputChanceLogics),
					new Dictionary<object, ChanceLogic>(recipe.TickInputChanceLogics),
					new Dictionary<object, ChanceLogic>(recipe.TickOutputChanceLogics),
					newConditions,
					new List<object>(recipe.IngredientActions),
					recipe.Data,
					recipe.Duration,
					recipe.RecipeCategory,
					recipe.GroupColor);
				copied.Parallels = recipe.Parallels * parallels;
				copied.SubtickParallels = recipe.SubtickParallels * subtickParallels;
				copied.OcLevel = recipe.OcLevel + addOCs;
				copied.BatchParallels = recipe.BatchParallels * batchParallels;
				if (RecipeDataUtil.GetBool(recipe.Data, "duration_is_total_cwu"))
					copied.Duration = (int)Math.Max(1, recipe.Duration * (1f - 0.025f * addOCs));
				else
					copied.Duration = Math.Max(1, durMod.Apply(recipe.Duration));
				if (!eutMod.Equals(ContentModifier.IDENTITY))
				{
					var preEUt = RecipeHelper.GetRealEUtWithIO(recipe);
					var eut = EURecipeCapability.CAP.CopyWithModifier(preEUt.Stack, eutMod);
					EURecipeCapability.PutEUContent(
						preEUt.IsInput ? copied.TickInputs : copied.TickOutputs, eut);
				}
				return copied;
			});
		}

		// Verbatim port of FunctionBuilder.applyAllButEU - applies the content
		// modifier to every capability EXCEPT EU (tick modifiers must not
		// rescale EUt contents; that's the eutModifier's job).
		private static Dictionary<object, List<Content.Content>> ApplyAllButEU(
			ContentModifier cm, Dictionary<object, List<Content.Content>> contents)
		{
			if (cm.Equals(ContentModifier.IDENTITY))
				return new Dictionary<object, List<Content.Content>>(contents);
			var copy = new Dictionary<object, List<Content.Content>>();
			foreach (var (cap, list) in contents)
			{
				if (list is null || list.Count == 0) continue;
				if (ReferenceEquals(cap, EURecipeCapability.CAP))
				{
					copy[cap] = new List<Content.Content>(list);
					continue;
				}
				var contentsCopy = new List<Content.Content>(list.Count);
				foreach (var content in list)
					contentsCopy.Add(content.Copy(cap, cm));
				copy[cap] = contentsCopy;
			}
			return copy;
		}
	}
}
