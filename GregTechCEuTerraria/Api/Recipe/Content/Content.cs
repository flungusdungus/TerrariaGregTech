#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;

namespace GregTechCEuTerraria.Api.Recipe.Content;

// LOCKED - verbatim port of pure-math surface of
// com.gregtechceu.gtceu.api.recipe.content.Content.
// DO NOT modify behavior; mirror upstream changes only.
//
// One typed-content entry in a recipe - an Object payload (resolved to K via
// the per-capability cast) carried alongside its chance (chance / maxChance),
// max chance, and per-tier chance boost.
//
// Documented deferrals (Forge / LDLib client-side UI; lands with recipe-
// browser polish):
//   - Mojang Codec serialization
//   - createOverlay / drawChance / drawRangeAmount / drawFluidAmount /
//     drawTick - client-side rendering helpers
//   - IGuiTexture / GuiGraphics / Font dependencies
public sealed class Content
{
	public object Payload { get; }
	public int Chance { get; }
	public int MaxChance { get; }
	public int TierChanceBoost { get; }

	public Content(object payload, int chance, int maxChance, int tierChanceBoost)
	{
		Payload = payload;
		Chance = chance;
		MaxChance = maxChance;
		TierChanceBoost = FixBoost(tierChanceBoost);
	}

	// Verbatim port of `copy(RecipeCapability<?>)` - duplicates this Content
	// with the capability's CopyInner applied to the payload. Untyped
	// `object` matches upstream's raw Object content type.
	public Content Copy(object capability) =>
		new(CopyPayloadVia(capability, Payload), Chance, MaxChance, TierChanceBoost);

	// Verbatim port of `copy(RecipeCapability<?>, ContentModifier)`.
	// IDENTITY modifier OR chanced-content -> shallow copy (don't apply
	// modifier to chanced rolls; that's CopyChanced's job).
	public Content Copy(object capability, ContentModifier modifier)
	{
		if (modifier.Equals(ContentModifier.IDENTITY) || Chance < MaxChance)
			return Copy(capability);
		return new Content(CopyPayloadVia(capability, Payload, modifier), Chance, MaxChance, TierChanceBoost);
	}

	// Verbatim port of `copyChanced(RecipeCapability<?>, ContentModifier)`.
	// Applies the modifier even on chanced contents (used by ChanceLogic.OR
	// when batching guaranteed multiples).
	public Content CopyChanced(object capability, ContentModifier modifier)
	{
		if (modifier.Equals(ContentModifier.IDENTITY))
			return Copy(capability);
		return new Content(CopyPayloadVia(capability, Payload, modifier), Chance, MaxChance, TierChanceBoost);
	}

	public bool IsChanced => Chance > 0 && Chance < MaxChance;

	// Verbatim port of fixBoost - adjusts a boost value for max-chance
	// scaling. Upstream's chance values run on a 10000-scale; if the recipe
	// uses a different scale, boosts get re-scaled here.
	private int FixBoost(int chanceBoost)
	{
		float error = (float)ChanceLogic.GetMaxChancedValue() / MaxChance;
		int fixed_ = (int)Math.Round(Math.Abs(chanceBoost) / error);
		return chanceBoost < 0 ? -fixed_ : fixed_;
	}

	public override string ToString() =>
		$"Content{{content={Payload}, chance={Chance}, maxChance={MaxChance}, tierChanceBoost={TierChanceBoost}}}";

	// Adapter helpers - capability is passed as object to avoid generic
	// variance issues. Concrete RecipeCapability<T>.CopyInner is invoked via
	// dynamic dispatch when consumers re-type. For the basic copy path
	// (no modifier) the payload is just passed through.
	// Dispatch through the capability's non-generic IRecipeCapability surface -
	// mirrors upstream's `capability.copyContent(content[, modifier])`. Every
	// concrete RecipeCapability<T> implements IRecipeCapability via the base
	// class, so this is unconditional. Without this dispatch, parallel /
	// overclock modifiers silently no-op on item + fluid contents (see
	// FluidRecipeCapability.CopyWithModifier / ItemRecipeCapability.CopyWithModifier).
	private static object CopyPayloadVia(object capability, object payload) =>
		((IRecipeCapability)capability).CopyContent(payload);
	private static object CopyPayloadVia(object capability, object payload, ContentModifier modifier) =>
		((IRecipeCapability)capability).CopyContent(payload, modifier);
	// NOTE: upstream's RecipeCapability has overloaded copyContent(T content)
	// and copyContent(T content, ContentModifier modifier). The latter
	// applies the modifier (e.g. multiplies ItemStack count by N). We need
	// to expose this on RecipeCapability when we port the typed-content
	// surface for items/fluids. For energy (EnergyStack) the payload is
	// immutable; modifier-aware copy is a no-op anyway.
}
