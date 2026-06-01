#nullable enable
using System;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Fluids;

// Immutable amount-of-fluid value. Empty if Type is null OR Amount <= 0;
// always check IsEmpty rather than comparing to default.
//
// Carries an optional TagCompound (Nbt) for fluid-specific extra data -
// upstream uses this for things like nuclear coolant radioactivity, gas
// pressure, fluid-block source coordinates. Two FluidStacks of the same
// type with DIFFERENT NBT cannot merge (matches Forge FluidStack
// .isFluidEqual semantics).
//
// Equality is by (type, amount, NBT); SameTypeAs compares (type, NBT) and
// is used by handlers to decide whether two stacks can merge.
public readonly struct FluidStack : IEquatable<FluidStack>
{
	public FluidType? Type { get; }
	public int Amount { get; }
	public TagCompound? Nbt { get; }

	public bool IsEmpty => Type is null || Amount <= 0;
	public static FluidStack Empty => default;

	public FluidStack(FluidType type, int amount, TagCompound? nbt = null)
	{
		Type = type;
		Amount = Math.Max(0, amount);
		Nbt = nbt;
	}

	public FluidStack WithAmount(int amount) =>
		Type is null || amount <= 0 ? Empty : new FluidStack(Type, amount, Nbt);

	// Same-fluid for merging purposes - type AND NBT match. Two stacks with
	// the same type but different NBT cannot stack (matches Forge).
	public bool SameTypeAs(FluidStack other)
	{
		if (Type is null || other.Type is null) return false;
		if (Type.Id != other.Type.Id) return false;
		return NbtEquals(Nbt, other.Nbt);
	}

	public bool Equals(FluidStack other) =>
		IsEmpty ? other.IsEmpty : (SameTypeAs(other) && Amount == other.Amount);

	public override bool Equals(object? obj) => obj is FluidStack other && Equals(other);
	public override int GetHashCode() => IsEmpty ? 0 : HashCode.Combine(Type!.Id, Amount, Nbt?.Count ?? 0);
	public override string ToString() => IsEmpty ? "<empty>" : $"{Amount} mB of {Type!.Id}";

	public static bool operator ==(FluidStack a, FluidStack b) => a.Equals(b);
	public static bool operator !=(FluidStack a, FluidStack b) => !a.Equals(b);

	// Deep-ish NBT equality - both null = equal; one null = unequal; both
	// present = compare tag values. We use TagCompound's own equality
	// semantics (key-by-key value compare) via ToString round-trip since
	// TagCompound doesn't override Equals. This is fine for the cell use
	// case (small NBTs); switch to a structural compare if hot.
	private static bool NbtEquals(TagCompound? a, TagCompound? b)
	{
		if (a is null && b is null) return true;
		if (a is null || b is null) return false;
		if (a.Count != b.Count) return false;
		// String-form compare - TagCompound.ToString serializes deterministically
		// because we always serialize via TagIO.
		return a.ToString() == b.ToString();
	}
}
