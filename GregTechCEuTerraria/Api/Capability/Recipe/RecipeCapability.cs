#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Recipe.Content;

namespace GregTechCEuTerraria.Api.Capability.Recipe;

// Non-generic dispatch surface for capability-keyed content copying. Mirrors
// upstream's `RecipeCapability.copyContent(Object, ContentModifier)` final
// methods - Content.CopyPayloadVia routes through this so it can dispatch
// without knowing T (capabilities are stored as `object` in recipe maps).
public interface IRecipeCapability
{
	object CopyContent(object content);
	object CopyContent(object content, ContentModifier modifier);

	// Non-generic dispatch surface for upstream's `shouldBypassDistinct()`.
	// Default false; `RecipeCapability<T>` overrides via the virtual method.
	// RecipeHandlerList reads this without knowing T.
	bool ShouldBypassDistinct() => false;
}

// LOCKED - port of
// com.gregtechceu.gtceu.api.capability.recipe.RecipeCapability.
// DO NOT modify behavior; mirror upstream changes only.
//
// Identity token for "this kind of resource flows through a recipe" - one
// concrete subclass per resource kind (EURecipeCapability, ItemRecipeCapability,
// FluidRecipeCapability, ...). RecipeLogic walks recipe contents grouped by
// capability and dispatches each group to handlers carrying the matching
// `getCapability()` return.
//
// Documented adaptations (deferred until full RecipeLogic port):
//   - Mojang Codec serialization (DIRECT_CODEC, contentCodec) dropped - we
//     load recipes via System.Text.Json from our own DTOs.
//   - DispatchedMapCodec / Content / ContentModifier / IContentSerializer
//     scaffolding dropped - these are the recipe-content modifier surface
//     that lands with the RecipeLogic trait port.
//   - GTRecipeTypeUI / Widget / WidgetGroup UI-binding methods dropped -
//     UI bindings live in our own MachineUILayout.
//   - FriendlyByteBuf network read/write dropped - we use tML packet
//     readers/writers in the recipe-sync port (when that lands).
//   - GTRegistries.RECIPE_CAPABILITIES registry dropped - we keep a flat
//     static list of capability instances below (matches upstream's
//     intent of "one canonical instance per resource kind").
//
// Minimum surface ported now:
//   - generic type parameter T (the resource type carried by recipes)
//   - copyInner(T) for content duplication during matching
//   - registry of all instances for capability-keyed iteration
public abstract class RecipeCapability<T> : IRecipeCapability
{
	public string Name { get; }

	protected RecipeCapability(string name)
	{
		Name = name;
		Register(this);
	}

	// Verbatim port of upstream's `copyInner(T)`. Used by `copyContent` on
	// IRecipeHandler to duplicate a recipe ingredient before mutating it
	// during the match loop.
	public abstract T CopyInner(T content);

	// Verbatim port of upstream's `copyWithModifier(T, ContentModifier)`.
	// Default = identity copy; per-capability overrides (Item/Fluid) actually
	// rescale the content's amount.
	public virtual T CopyWithModifier(T content, ContentModifier modifier) => CopyInner(content);

	// Verbatim port of upstream's `final copyContent(Object[, ContentModifier])`.
	// Non-generic dispatch surface for Content.CopyPayloadVia.
	object IRecipeCapability.CopyContent(object content) => CopyInner((T)content)!;
	object IRecipeCapability.CopyContent(object content, ContentModifier modifier) =>
		CopyWithModifier((T)content, modifier)!;

	// Verbatim port of upstream's `of(Object)` - resolves a raw content payload
	// into the typed `T` for this capability. Upstream delegates to a
	// per-capability serializer that can coerce arbitrary inputs (KubeJS recipe
	// builder); our content payloads come from `IngredientJson` already typed,
	// so the default is a direct cast. Subclasses can override if needed.
	public virtual T Of(object content) => (T)content;

	// Verbatim port of upstream's `shouldBypassDistinct()` default - if true,
	// recipe matching ignores BUS_DISTINCT grouping for this capability.
	// EU capability typically bypasses (energy is always pooled across the
	// machine regardless of input-bus distinction).
	public virtual bool ShouldBypassDistinct() => false;

	// === Registry ============================================================
	// Flat list of capability instances. Matches upstream's
	// `GTRegistries.RECIPE_CAPABILITIES` purpose without the Mojang registry
	// infrastructure.

	private static readonly List<object> _registry = new();
	private static void Register(object cap) { lock (_registry) _registry.Add(cap); }
	public static IReadOnlyList<object> All { get { lock (_registry) return _registry.ToArray(); } }
}
