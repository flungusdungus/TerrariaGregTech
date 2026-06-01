#nullable enable
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Fluids.Attribute;

namespace GregTechCEuTerraria.Api.Fluids;

// A registered fluid kind. Mirrors upstream's `GTFluid extends FlowingFluid`
// in semantics - Terraria has no FlowingFluid block primitive so this is a
// pure data class. Identity by Id (string), not by reference, so fluids
// survive save/load and cross-mod equality.
//
// Built via FluidBuilder (see Fluids/FluidBuilder.cs) and registered into
// FluidRegistry. The four well-known built-ins (water/lava/steam/distilled_
// water) are registered here statically; everything else is registered by
// FluidLoader at Mod.Load from material data.
//
// Upstream Forge property mapping:
//   FluidType.color           -> Color
//   FluidType.temperature     -> Temperature   (Kelvin)
//   FluidType.density         -> Density       (signed; positive sinks, negative rises)
//   FluidType.luminosity      -> Luminosity    (0..15)
//   FluidType.viscosity       -> Viscosity     (1000 = water baseline)
//   GTFluid.burnTime          -> BurnTime      (combustion-engine fuel rating)
//   IAttributedFluid          -> Attributes
//   FluidState                -> State         (LIQUID/GAS/PLASMA)
//   FluidStorageKey           -> SourceKey     (registration slot on source material)
public sealed class FluidType
{
	public string Id { get; }
	public string DisplayName { get; }
	public uint Color { get; }
	// Mirrors upstream FluidBuilder.isColorEnabled - false means the fluid
	// ships a baked custom texture and should NOT be color-tinted when drawn.
	public bool IsColorEnabled { get; }
	public FluidState State { get; }
	public int Temperature { get; }
	public int Density { get; }
	public int Luminosity { get; }
	public int Viscosity { get; }
	// Combustion-engine fuel rating in EU per mB burned (upstream's
	// `burnTime`). 0 = not burnable. Read by semi-fluid combustion generators
	// to gate fluid acceptance + size their EU output.
	public int BurnTime { get; }
	// Whether this fluid generates an in-world fluid block (upstream
	// FluidBuilder.hasFluidBlock). Carried for parity; no Terraria fluid
	// placement yet.
	public bool HasFluidBlock { get; }
	// Whether a bucket item is generated for this fluid (upstream
	// FluidBuilder.hasBucket - true for almost every fluid). Drives the
	// per-fluid bucket items.
	public bool HasBucket { get; }

	// FluidStorageKey under which this fluid was registered on its source
	// material. Null for fluids registered ad-hoc (the four built-ins below).
	public FluidStorageKey? SourceKey { get; }
	// Source material id, if registered from a Material's FluidProperty.
	public string? SourceMaterialId { get; }

	private readonly List<FluidAttribute> _attributes;
	public IReadOnlyList<FluidAttribute> Attributes => _attributes;

	// Full-parity ctor used by FluidBuilder.Build. The legacy 3-arg ctor below
	// stays as a compat shim for the four hard-coded fluids in FluidRegistry.
	internal FluidType(
		string id,
		string displayName,
		uint color,
		bool isColorEnabled,
		FluidState state,
		int temperature,
		int density,
		int luminosity,
		int viscosity,
		int burnTime,
		bool hasFluidBlock,
		bool hasBucket,
		FluidStorageKey? sourceKey,
		string? sourceMaterialId,
		IEnumerable<FluidAttribute>? attributes)
	{
		Id = id;
		DisplayName = displayName;
		Color = color;
		IsColorEnabled = isColorEnabled;
		State = state;
		Temperature = temperature;
		Density = density;
		Luminosity = luminosity;
		Viscosity = viscosity;
		BurnTime = burnTime;
		HasFluidBlock = hasFluidBlock;
		HasBucket = hasBucket;
		SourceKey = sourceKey;
		SourceMaterialId = sourceMaterialId;
		_attributes = attributes?.ToList() ?? new List<FluidAttribute>();
	}

	// Legacy ctor for the four built-in fluids. State defaults to LIQUID;
	// temperature etc. inferred from defaults. Prefer FluidBuilder for any
	// new registration.
	public FluidType(string id, string displayName, uint color)
		: this(id, displayName, color, isColorEnabled: true, FluidState.LIQUID,
			temperature: FluidConstants.ROOM_TEMPERATURE, density: FluidConstants.DEFAULT_LIQUID_DENSITY,
			luminosity: 0, viscosity: FluidConstants.DEFAULT_LIQUID_VISCOSITY,
			burnTime: -1, hasFluidBlock: false, hasBucket: true,
			sourceKey: null, sourceMaterialId: null, attributes: null)
	{ }

	// Verbatim port of upstream's `containsAttribute(FluidAttribute)`.
	// FluidAttribute is a concrete final class; identity is by Id (via the
	// type's Equals override), not by subclass type. Callers compare against
	// known instances from `FluidAttributes.*`.
	public bool HasAttribute(FluidAttribute attr) => _attributes.Contains(attr);

	public bool IsGaseous => State == FluidState.GAS;
	public bool IsPlasma  => State == FluidState.PLASMA;

	public override string ToString() => Id;
}

// Process-wide fluid registry. Hard-coded built-ins below + everything
// FluidLoader registers at Mod.Load from material data.
public static class FluidRegistry
{
	private static readonly Dictionary<string, FluidType> _byId = new();

	public static readonly FluidType Water           = Register(new FluidType("water", "Water", 0x3C64DC));
	// Lava: vanilla fluid (`minecraft:lava` resolves here after namespace strip).
	// Orange-red consistent with Terraria's lava palette.
	public static readonly FluidType Lava            = Register(new FluidType("lava", "Lava", 0xFF6818));
	// Steam: the boiler's output, the steam turbine's input. Light grey-blue
	// tint so it reads as "hot vapor" next to deep-blue water.
	public static readonly FluidType Steam           = Register(new FluidType("steam", "Steam", 0xC8D8E8));
	// Distilled water: steam turbine byproduct (condensate).
	public static readonly FluidType DistilledWater  = Register(new FluidType("distilled_water", "Distilled Water", 0x88BBE0));

	public static FluidType Register(FluidType fluid)
	{
		_byId[fluid.Id] = fluid;
		return fluid;
	}

	public static FluidType? Get(string id) => _byId.GetValueOrDefault(id);

	public static bool TryGet(string id, out FluidType type)
	{
		if (_byId.TryGetValue(id, out var found))
		{
			type = found;
			return true;
		}
		type = null!;
		return false;
	}

	public static IReadOnlyCollection<FluidType> All => _byId.Values;
}
