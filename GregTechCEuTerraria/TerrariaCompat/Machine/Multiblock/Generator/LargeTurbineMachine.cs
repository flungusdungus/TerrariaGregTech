#nullable enable
using System;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Generator;

// Port of LargeTurbineMachine. Rotor-powered generator multi - runs
// <fuel>_turbine_fuels recipes (steam/gas/plasma) and emits EU via OUTPUT_ENERGY
// hatches. Output voltage scales with rotor power x efficiency; duration scales
// inversely with efficiency. Rotor durability dropped (Terraria items don't wear).
public sealed class LargeTurbineMachine : WorkableElectricMultiblockMachine
{
	public LargeTurbineMachine() : base() { }

	private int TierIdx => (int)Tier;

	// Upstream BASE_EU_OUTPUT = V[tier] * 2.
	private long BaseEuOutput => VoltageTiers.V(TierIdx) * 2L;

	// Generator-multi tier override: base GetTier returns hatch-derived MultiTier,
	// but rotor-holder math (tierDifference = rotorTier - controllerTier -> 2^diff
	// power multiplier) needs the controller's intrinsic design tier. Without
	// this, an HV rotor on an EV turbine reports diff=0 instead of upstream's
	// diff=-1, producing a wrong power curve.
	public override int GetTier() => TierIdx;

	public RotorHolderPartMachine? GetRotorHolder()
	{
		foreach (var part in GetParts())
		{
			if (part is RotorHolderPartMachine rh) return rh;
		}
		return null;
	}

	public override bool RegressWhenWaiting() => false;

	// Verbatim: turbines void everything they can't deposit; excess EU lost.
	public override bool CanVoidRecipeOutputs(object capability) => true;

	// Rotor-power-scaled, NOT hatch-voltage-derived.
	public override long OverclockVoltage
	{
		get
		{
			var rh = GetRotorHolder();
			if (rh is not null && rh.HasRotor())
				return BaseEuOutput * rh.GetTotalPower() / 100L;
			return 0;
		}
	}

	// (currentSpeed / maxSpeed)^2 - quadratic ramp-up.
	private double ProductionBoost()
	{
		var rh = GetRotorHolder();
		if (rh is not null && rh.HasRotor())
		{
			int maxSpeed     = rh.MaxRotorHolderSpeed;
			int currentSpeed = rh.RotorSpeed;
			if (currentSpeed >= maxSpeed) return 1;
			return System.Math.Pow(1.0 * currentSpeed / maxSpeed, 2);
		}
		return 0;
	}

	public override RecipeModifier GetRecipeModifier() => TurbineModifier;

	// Cancel reasons surface actionable text via the locale (port-locale.py _RECIPE_STATUS).
	private const string ReasonNoRotor      = "gtceu.recipe_modifier.no_rotor";
	private const string ReasonVoltageTooLow = "gtceu.recipe_modifier.turbine_voltage_too_low";

	private static readonly RecipeModifier TurbineModifier = new((machine, recipe) =>
	{
		if (machine is not LargeTurbineMachine turbineMachine) return ModifierFunction.NULL;
		var rotorHolder = turbineMachine.GetRotorHolder();
		if (rotorHolder is null) return ModifierFunction.NULL;

		Api.Recipe.Ingredient.EnergyStack EUt = recipe.OutputEUt;
		long turbineMaxVoltage = turbineMachine.OverclockVoltage;
		double holderEfficiency = rotorHolder.GetTotalEfficiency() / 100.0;
		if (EUt.IsEmpty() || turbineMaxVoltage <= EUt.Voltage || holderEfficiency <= 0)
			return ModifierFunction.Cancel(rotorHolder.HasRotor() ? ReasonVoltageTooLow : ReasonNoRotor);

		// Ceiling, not floor - guarantees the desired output voltage.
		int maxParallel = (int)(turbineMaxVoltage / EUt.GetTotalEU());
		if (turbineMaxVoltage % EUt.GetTotalEU() != 0) maxParallel++;

		int actualParallel = ParallelLogic.GetParallelAmountFast(turbineMachine, recipe, maxParallel);
		double eutMultiplier = (maxParallel == actualParallel)
			? turbineMachine.ProductionBoost() * turbineMaxVoltage / EUt.Voltage
			: turbineMachine.ProductionBoost() * actualParallel;

		return ModifierFunction.Builder()
			.InputModifier(ContentModifier.Multiplier_(actualParallel))
			.OutputModifier(ContentModifier.Multiplier_(actualParallel))
			.EutMultiplier(eutMultiplier)
			.Parallels(actualParallel)
			.DurationMultiplier(holderEfficiency)
			.Build();
	});

	// Mirrors upstream's "rotor_speed" + "efficiency" lines from addDisplayText.
	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		var rh = GetRotorHolder();
		if (rh is null || !rh.HasRotor()) return;
		lines.Add($"Rotor: {rh.RotorSpeed:N0} / {rh.MaxRotorHolderSpeed:N0} RPM");
		int eff = rh.GetTotalEfficiency();
		if (eff > 0) lines.Add($"Efficiency: {eff}%");
	}
}
