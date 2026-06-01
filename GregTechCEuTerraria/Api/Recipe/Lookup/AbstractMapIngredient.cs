#nullable enable
using System;

namespace GregTechCEuTerraria.Api.Recipe.Lookup;

// PORTED - verbatim port of
// com.gregtechceu.gtceu.api.recipe.lookup.ingredient.AbstractMapIngredient.
//
// Base node-key for the recipe-lookup trie (RecipeDB / Branch). A recipe's
// inputs are decomposed into AbstractMapIngredients; the trie keys each level
// on one. The base equals() compares only the concrete class - subclasses
// override and call base.Equals first, then compare their own payload.
//
// hashCode is cached (computed once, lazily) - recipe-lookup hashes these keys
// constantly, and the payloads are immutable once built.
public abstract class AbstractMapIngredient
{
	// objClass upstream - the concrete type, captured at construction so
	// equals() can do a fast same-class gate before payload comparison.
	protected readonly Type ObjClass;

	private int  _hash;
	private bool _hashed;

	protected AbstractMapIngredient()
	{
		ObjClass = GetType();
	}

	protected abstract int Hash();

	public sealed override int GetHashCode()
	{
		if (!_hashed)
		{
			_hash   = Hash();
			_hashed = true;
		}
		return _hash;
	}

	public sealed override bool Equals(object? obj)
	{
		if (ReferenceEquals(this, obj)) return true;
		if (obj is AbstractMapIngredient other && ObjClass == other.ObjClass)
			return EqualsSameClass(other);
		return false;
	}

	// Payload equality - called only when `other` is the SAME concrete class.
	// Base returns true: for an ingredient whose identity IS its class, the
	// objClass gate is the whole comparison. Subclasses with a payload override.
	//
	// Documented adaptation: upstream's AbstractMapIngredient.equals is itself
	// overridable, and subclasses override it (calling super.equals first). C#
	// warns (CS0659) when Equals is overridden without GetHashCode, and our
	// GetHashCode is the sealed cached-Hash mechanism - so Equals is sealed
	// here and subclass payload comparison is routed through this hook.
	protected virtual bool EqualsSameClass(AbstractMapIngredient other) => true;

	// Special ingredients live in Branch.specialNodes - their hashes collide
	// (deliberately) so they must be differentiated by equality, not hash.
	// Default false; the intersection / NBT-predicate ingredients override.
	public virtual bool IsSpecialIngredient() => false;
}
