#nullable enable
using System;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - port of
// com.gregtechceu.gtceu.api.recipe.ingredient.EnergyStack.
// DO NOT modify behavior; mirror upstream changes only.
//
// Immutable (voltage, amperage) tuple representing an energy "amount" in a
// recipe - analogous to ItemStack/FluidStack for energy. RecipeLogic's
// handleRecipeIO(IO io, GTRecipe, List<EnergyStack> left, simulate) walks
// this list and drains/fills NotifiableEnergyContainer accordingly.
//
// Documented adaptations (deferred until RecipeLogic port):
//   - Mojang Codec serialization (FULL_CODEC, VOLTAGE_ONLY_CODEC, CODEC)
//     dropped - we load recipes via System.Text.Json from our own DTO.
//   - FriendlyByteBuf toNetwork/fromNetwork dropped - we use tML packet
//     readers/writers when recipe sync lands.
//   - WithIO inner record (energy + IO direction tag for ingredient lists)
//     deferred - only used by RecipeLogic's handleRecipeIO, not yet ported.
//   - `@With` Lombok auto-with methods -> explicit `WithVoltage`/`WithAmperage`.
public readonly struct EnergyStack : IEquatable<EnergyStack>
{
	public long Voltage  { get; }
	public long Amperage { get; }

	public EnergyStack(long voltage, long amperage)
	{
		if (amperage < 1)
			throw new ArgumentException("Amperage must be >= 1", nameof(amperage));
		if (voltage < 0)
			throw new ArgumentException("Voltage must be >= 0", nameof(voltage));
		Voltage = voltage;
		Amperage = amperage;
	}

	// Verbatim port of upstream's voltage-only constructor (1A uses).
	public EnergyStack(long voltage) : this(voltage, 1) { }

	public static readonly EnergyStack EMPTY = new(0, 1);
	public static readonly EnergyStack MAX   = new(long.MaxValue, 1);

	public long GetTotalEU() => Voltage * Amperage;

	public bool IsEmpty() => Equals(EMPTY) || Voltage <= 0;

	// Verbatim port of upstream `withVoltage` / `withAmperage` (Lombok @With).
	public EnergyStack WithVoltage(long voltage)   => new(voltage, Amperage);
	public EnergyStack WithAmperage(long amperage) => new(Voltage, amperage);

	public EnergyStack Add(long voltage, long amperage)
	{
		if (Voltage + voltage < 0)
			throw new ArgumentException("Resulting voltage must be >= 0");
		if (Amperage + amperage < 1)
			throw new ArgumentException("Resulting amperage must be >= 1");
		return new EnergyStack(Voltage + voltage, Amperage + amperage);
	}

	public EnergyStack AddVoltage(long voltage) => WithVoltage(Voltage + voltage);

	public EnergyStack MultiplyVoltage(long   multiplier) => WithVoltage(Voltage * multiplier);
	public EnergyStack MultiplyVoltage(double multiplier) => WithVoltage((long)(Voltage * multiplier));

	public EnergyStack AddAmperage(long amperage)      => WithAmperage(Amperage + amperage);
	public EnergyStack MultiplyAmperage(long multiplier) => WithAmperage(Amperage * multiplier);

	public static EnergyStack Sum(EnergyStack a, EnergyStack b) => a.Add(b.Voltage, b.Amperage);

	public bool Equals(EnergyStack other) => Voltage == other.Voltage && Amperage == other.Amperage;
	public override bool Equals(object? obj) => obj is EnergyStack es && Equals(es);
	public override int GetHashCode() => HashCode.Combine(Voltage, Amperage);
	public override string ToString() => $"EnergyStack({Voltage}V, {Amperage}A)";

	public static bool operator ==(EnergyStack a, EnergyStack b) => a.Equals(b);
	public static bool operator !=(EnergyStack a, EnergyStack b) => !a.Equals(b);
}
