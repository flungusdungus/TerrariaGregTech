#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Block;

// Port of com.gregtechceu.gtceu.api.block.ICoilType.
//
// Heating-coil tier descriptor. Eight upstream tiers (Cupronickel through
// Tritanium). EBF + Multi Smelter + Pyrolyse Oven all consume `Temperature`
// (for `ebf_temp` gate) + `Level` (parallel bonus) + `EnergyDiscount` (EUt
// multiplier). The recipe pipeline reads these via
// `MultiblockState.MatchContext("CoilType")` populated by
// `Predicates.HeatingCoils()`.
//
// Adaptations:
//   - `Material` accessor dropped (we don't surface material refs on coils).
//   - `Tier = ordinal` collapsed to an explicit constructor arg.
//   - The upstream `CoilBlock.CoilType` enum is non-tML; we use static
//     instances. Lookup helpers exposed for the predicate + registry init.
public interface ICoilType
{
	string Name { get; }
	int Tier { get; }
	int Temperature   { get; }  // Kelvin; recipe.ebf_temp gate
	int Level         { get; }  // Multi Smelter parallel level
	int EnergyDiscount{ get; }  // EUt multiplier (Pyrolyse Oven)
}

// Concrete 8-coil registry. Mirrors upstream `CoilBlock.CoilType` enum verbatim
// (temperature / level / energy-discount). `TileName` is the bare upstream id
// (e.g. `cupronickel_coil_block`) so we can resolve each tile through the
// `CasingRegistry` lookup at first-match time.
public sealed class CoilType : ICoilType
{
	public static readonly CoilType CUPRONICKEL = new("cupronickel", 0, 1800, 1, 1, "cupronickel_coil_block");
	public static readonly CoilType KANTHAL     = new("kanthal",     1, 2700, 2, 1, "kanthal_coil_block");
	public static readonly CoilType NICHROME    = new("nichrome",    2, 3600, 2, 2, "nichrome_coil_block");
	public static readonly CoilType RTM_ALLOY   = new("rtm_alloy",   3, 4500, 4, 2, "rtm_alloy_coil_block");
	public static readonly CoilType HSSG        = new("hssg",        4, 5400, 4, 4, "hssg_coil_block");
	public static readonly CoilType NAQUADAH    = new("naquadah",    5, 7200, 8, 4, "naquadah_coil_block");
	public static readonly CoilType TRINIUM     = new("trinium",     6, 9001, 8, 8, "trinium_coil_block");
	public static readonly CoilType TRITANIUM   = new("tritanium",   7,10800,16, 8, "tritanium_coil_block");

	public string Name { get; }
	public int Tier { get; }
	public int Temperature    { get; }
	public int Level          { get; }
	public int EnergyDiscount { get; }
	public string TileName    { get; }

	private CoilType(string name, int tier, int temperature, int level, int energyDiscount, string tileName)
	{
		Name = name; Tier = tier;
		Temperature = temperature; Level = level; EnergyDiscount = energyDiscount;
		TileName = tileName;
	}

	public static readonly CoilType[] All =
	{
		CUPRONICKEL, KANTHAL, NICHROME, RTM_ALLOY, HSSG, NAQUADAH, TRINIUM, TRITANIUM,
	};

	public static CoilType? GetByName(string? name)
	{
		if (name is null) return null;
		foreach (var c in All) if (c.Name == name) return c;
		return null;
	}
}

// Default no-coil placeholder - kept for `CoilWorkableElectricMultiblockMachine`'s
// pre-form default value. Mirrors upstream's initial `CoilBlock.CoilType.CUPRONICKEL`
// fallback.
public sealed class DefaultCoilType : ICoilType
{
	public static readonly DefaultCoilType CUPRONICKEL = new(
		"cupronickel", 0, 1800, 1, 1);

	public string Name { get; }
	public int Tier { get; }
	public int Temperature    { get; }
	public int Level          { get; }
	public int EnergyDiscount { get; }

	private DefaultCoilType(string name, int tier, int temp, int level, int discount)
	{
		Name = name; Tier = tier; Temperature = temp; Level = level; EnergyDiscount = discount;
	}
}
