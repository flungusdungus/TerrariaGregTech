#nullable enable
using System.Collections.Generic;
using System.Text;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Fluids.Attribute;
using GregTechCEuTerraria.Api.Fluids.Store;

namespace GregTechCEuTerraria.Api.Fluids;

// Verbatim port of com.gregtechceu.gtceu.api.fluids.FluidBuilder.
//
// Fluent builder for a material's fluid. The material declares it (via the
// JSON `fluids` array -> MaterialJsonLoader enqueues a FluidBuilder, mirroring
// upstream Material.Builder.fluid()/liquid()/gas()/plasma()), and Build()
// resolves the final FluidType.
//
// Most values are left at INFER and resolved at Build() time by Determine* -
// verbatim ports of upstream's determineTemperature/Color/Density/Luminosity/
// Viscosity, which read material context (blast temperature, the DUST form,
// PHOSPHORESCENT / STICKY flags, material RGB, fluid state).
//
// Documented adaptations:
//   - GTRegistrate / Forge fluid + block + bucket registration dropped -
//     Terraria has no FlowingFluid block, no fluid bucket item, no still/
//     flowing texture pair. `block`/`disableBucket`/`customStill` are accepted
//     (so the extracted data stays complete) but only `isColorEnabled` of the
//     texture-side flags affects our output.
//   - build(Material, key, registrate) -> Build(Material?, key) returning the
//     resolved FluidType directly.
public sealed class FluidBuilder
{
	internal const int  INFER_TEMPERATURE = -1;
	internal const uint INFER_COLOR       = 0xFFFFFFFF;
	internal const int  INFER_DENSITY     = -1;
	internal const int  INFER_LUMINOSITY  = -1;
	internal const int  INFER_VISCOSITY   = -1;

	private string? _name;
	private string? _translation;
	private readonly List<FluidAttribute> _attributes = new();

	private FluidState? _state;
	private int  _temperature   = INFER_TEMPERATURE;
	private uint _color         = INFER_COLOR;
	private bool _isColorEnabled = true;
	private int  _density       = INFER_DENSITY;
	private int  _luminosity    = INFER_LUMINOSITY;
	private int  _viscosity     = INFER_VISCOSITY;
	private int  _burnTime      = -1;
	private bool _hasFluidBlock;        // upstream default false - opt-in via .block()
	private bool _hasBucket = true;     // upstream default true  - every fluid gets a bucket

	// === Fluent setters (mirror upstream method names) ======================

	public FluidBuilder Name(string name)             { _name = name; return this; }
	public FluidBuilder Translation(string t)         { _translation = t; return this; }
	public FluidBuilder State(FluidState state)       { _state = state; return this; }

	public FluidBuilder Temperature(int kelvin)
	{
		if (kelvin < 0) throw new System.ArgumentException("temperature must be >= 0");
		_temperature = kelvin;
		return this;
	}

	// Color may be RGB or ARGB; RGB is promoted to opaque ARGB. A fully-white
	// result equals INFER, so upstream treats it as "disable color".
	public FluidBuilder Color(uint color)
	{
		_color = ConvertRgbToArgb(color);
		if (_color == INFER_COLOR) return DisableColor();
		return this;
	}

	public FluidBuilder DisableColor() { _isColorEnabled = false; return this; }

	public FluidBuilder Density(int density) { _density = density; return this; }

	public FluidBuilder Luminosity(int luminosity)
	{
		if (luminosity < 0 || luminosity >= 16)
			throw new System.ArgumentException("luminosity must be >= 0 and < 16");
		_luminosity = luminosity;
		return this;
	}

	public FluidBuilder Viscosity(int mcViscosity)
	{
		if (mcViscosity < 0) throw new System.ArgumentException("viscosity must be >= 0");
		_viscosity = mcViscosity;
		return this;
	}

	// Combustion fuel rating. Upstream default -1 (unset).
	public FluidBuilder BurnTime(int burnTime) { _burnTime = burnTime; return this; }

	public FluidBuilder Attribute(FluidAttribute attr) { _attributes.Add(attr); return this; }
	// Whether an in-world fluid block is generated. Carried through to FluidType
	// but not yet acted on (no fluid placement in Terraria for now).
	public FluidBuilder Block() { _hasFluidBlock = true; return this; }
	// Whether a bucket item is generated for this fluid - true by default.
	// Drives the per-fluid bucket items (creative-mode fluid handling / testing).
	public FluidBuilder DisableBucket() { _hasBucket = false; return this; }

	// === Build ==============================================================

	// Resolve the fluid's registry id without building - used by FluidProperty
	// for the skip-if-already-registered check. Verbatim of determineName.
	public string ResolveName(Material material, FluidStorageKey key) =>
		_name ?? key.FluidIdFor(material);

	// Verbatim port of FluidBuilder.build. `material` is null only for fluids
	// registered with no source material (upstream's Material.NULL); our four
	// built-ins take that path but they bypass FluidBuilder entirely.
	public FluidType Build(Material? material, FluidStorageKey? key)
	{
		if (_name is null && material is not null && key is not null)
			_name = key.FluidIdFor(material);
		if (_name is null)
			throw new System.InvalidOperationException("Could not determine fluid name");

		FluidState state = _state ?? key?.DefaultState ?? FluidState.LIQUID;

		DetermineTemperature(material, state);
		DetermineColor(material);
		DetermineDensity(state);
		DetermineLuminosity(material, state);
		DetermineViscosity(material, state);

		string displayName = _translation ?? HumanizeId(_name);
		return new FluidType(_name, displayName, _color, _isColorEnabled, state,
			_temperature, _density, _luminosity, _viscosity, _burnTime,
			_hasFluidBlock, _hasBucket, key, material?.Id, _attributes);
	}

	// === Determine* - verbatim ports ========================================

	private void DetermineTemperature(Material? material, FluidState state)
	{
		if (_temperature != INFER_TEMPERATURE) return;
		if (material is null)
		{
			_temperature = FluidConstants.ROOM_TEMPERATURE;
			return;
		}
		if (material.BlastTemperatureK is null)
		{
			_temperature = state switch
			{
				FluidState.LIQUID => material.Forms.Contains("DUST")
					? FluidConstants.SOLID_LIQUID_TEMPERATURE
					: FluidConstants.ROOM_TEMPERATURE,
				FluidState.GAS => FluidConstants.ROOM_TEMPERATURE,
				FluidState.PLASMA => DeterminePlasmaTemperature(material),
				_ => FluidConstants.ROOM_TEMPERATURE,
			};
		}
		else
		{
			_temperature = material.BlastTemperatureK.Value + state switch
			{
				FluidState.LIQUID => FluidConstants.LIQUID_TEMPERATURE_OFFSET,
				FluidState.GAS    => FluidConstants.GAS_TEMPERATURE_OFFSET,
				FluidState.PLASMA => FluidConstants.BASE_PLASMA_TEMPERATURE,
				_ => 0,
			};
		}
	}

	// Plasma without a blast temperature: BASE_PLASMA, plus the material's
	// primary (non-plasma) fluid builder's resolved temperature when it has
	// one. Registration priority guarantees that primary builder is already
	// built, so its _temperature field is concrete here.
	private static int DeterminePlasmaTemperature(Material material)
	{
		if (material.HasFluid())
		{
			var primary = material.GetFluidBuilder();
			if (primary is not null && !ReferenceEquals(primary, material.GetFluidBuilder(FluidStorageKey.PLASMA)))
				return FluidConstants.BASE_PLASMA_TEMPERATURE + primary._temperature;
		}
		return FluidConstants.BASE_PLASMA_TEMPERATURE;
	}

	private void DetermineColor(Material? material)
	{
		if (_color != INFER_COLOR) return;
		if (_isColorEnabled && material is not null)
			_color = ConvertRgbToArgb(material.Color ?? 0xFFFFFFu);
	}

	private void DetermineDensity(FluidState state)
	{
		if (_density != INFER_DENSITY) return;
		_density = state switch
		{
			FluidState.LIQUID => FluidConstants.DEFAULT_LIQUID_DENSITY,
			FluidState.GAS    => FluidConstants.DEFAULT_GAS_DENSITY,
			FluidState.PLASMA => FluidConstants.DEFAULT_PLASMA_DENSITY,
			_ => FluidConstants.DEFAULT_LIQUID_DENSITY,
		};
	}

	private void DetermineLuminosity(Material? material, FluidState state)
	{
		if (_luminosity != INFER_LUMINOSITY) return;
		if (state == FluidState.PLASMA)
			_luminosity = 15;
		else if (material is not null)
		{
			if (material.Flags.Contains("PHOSPHORESCENT"))
				_luminosity = 15;
			else if (state == FluidState.LIQUID && material.Forms.Contains("DUST"))
				_luminosity = 10;
			else
				_luminosity = 0;
		}
		else
			_luminosity = 0;
	}

	private void DetermineViscosity(Material? material, FluidState state)
	{
		if (_viscosity != INFER_VISCOSITY) return;
		_viscosity = state switch
		{
			FluidState.LIQUID => material is not null && material.Flags.Contains("STICKY")
				? FluidConstants.STICKY_LIQUID_VISCOSITY
				: FluidConstants.DEFAULT_LIQUID_VISCOSITY,
			FluidState.GAS    => FluidConstants.DEFAULT_GAS_VISCOSITY,
			FluidState.PLASMA => FluidConstants.DEFAULT_PLASMA_VISCOSITY,
			_ => FluidConstants.DEFAULT_LIQUID_VISCOSITY,
		};
	}

	// Verbatim port of GTUtil.convertRGBtoARGB - promote an RGB value to opaque
	// ARGB if it carries no alpha.
	private static uint ConvertRgbToArgb(uint color) =>
		(color & 0xFF000000u) == 0 ? color | 0xFF000000u : color;

	// "molten_iron" -> "Molten Iron", "iron_plasma" -> "Iron Plasma".
	private static string HumanizeId(string id)
	{
		var sb = new StringBuilder(id.Length);
		bool capNext = true;
		foreach (char c in id)
		{
			if (c == '_') { sb.Append(' '); capNext = true; continue; }
			sb.Append(capNext ? char.ToUpperInvariant(c) : c);
			capNext = false;
		}
		return sb.ToString();
	}
}
