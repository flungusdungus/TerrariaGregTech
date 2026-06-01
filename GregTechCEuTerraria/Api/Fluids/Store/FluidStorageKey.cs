#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Data.Chemical.Material;

namespace GregTechCEuTerraria.Api.Fluids.Store;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.fluids.store
// .FluidStorageKey + FluidStorageKeys.
//
// Key identifying one of the registration slots a material's FluidProperty can
// occupy. A material can register several (LIQUID + PLASMA, GAS + MOLTEN, ...).
//
//   LIQUID  - `<name>` if it's the primary form, else `liquid_<name>`. LIQUID state.
//   GAS     - `<name>` if it's the primary form, else `<name>_gas`.        GAS state.
//   PLASMA  - `<name>_plasma`.                                             PLASMA state.
//   MOLTEN  - `molten_<name>`.                                             LIQUID state.
//
// The registry-name function takes the whole Material because LIQUID/GAS need
// the material's FluidProperty.PrimaryKey to decide between the bare and
// prefixed/postfixed form (verbatim upstream prefixedRegisteredName).
public sealed class FluidStorageKey
{
	public string Id { get; }
	public FluidState DefaultState { get; }

	// Material -> fluid registry id for this key. Mirrors upstream's
	// `registryNameFunction`.
	internal System.Func<Material, string> RegistryNameFunction { get; }

	// Material -> i18n translation key. Mirrors upstream's `translationKeyFunction`.
	internal System.Func<Material, string> TranslationKeyFunction { get; }

	// MaterialIconType id used by this key for textured rendering. Mirrors
	// upstream's `iconType`.
	public string IconType { get; }

	// Higher numbers register first (upstream sorts by `-registrationPriority`).
	// LIQUID/GAS = 0, PLASMA/MOLTEN = -1, so the primary liquid/gas builds
	// before plasma/molten - plasma temperature inference reads the already-
	// built primary builder.
	public int RegistrationPriority { get; }

	private FluidStorageKey(
		string id,
		FluidState defaultState,
		System.Func<Material, string> registryNameFunction,
		System.Func<Material, string> translationKeyFunction,
		string iconType,
		int registrationPriority)
	{
		Id = id;
		DefaultState = defaultState;
		RegistryNameFunction = registryNameFunction;
		TranslationKeyFunction = translationKeyFunction;
		IconType = iconType;
		RegistrationPriority = registrationPriority;
		_byId[id] = this;
	}

	public string FluidIdFor(Material material) => RegistryNameFunction(material);
	public string TranslationKeyFor(Material material) => TranslationKeyFunction(material);

	private static readonly Dictionary<string, FluidStorageKey> _byId = new();
	public static FluidStorageKey? Get(string id) => _byId.GetValueOrDefault(id);
	public static IReadOnlyCollection<FluidStorageKey> All => _byId.Values;

	// === Built-in keys (verbatim mirror of FluidStorageKeys.java) ===============

	public static readonly FluidStorageKey LIQUID =
		new("liquid", FluidState.LIQUID,
			// LIQUID! - the field is non-null by the time this lambda runs;
			// the `!` only silences the self-reference-in-initializer warning.
			m => PrefixedRegisteredName("liquid_", LIQUID!, m),
			m => m.Forms.Contains("DUST") ? "gtceu.fluid.liquid_generic" : "gtceu.fluid.generic",
			iconType: "liquid",
			registrationPriority: 0);

	public static readonly FluidStorageKey GAS =
		new("gas", FluidState.GAS,
			m => PostfixedRegisteredName("_gas", GAS!, m),
			m => m.Forms.Contains("DUST") ? "gtceu.fluid.gas_vapor" : "gtceu.fluid.generic",
			iconType: "gas",
			registrationPriority: 0);

	public static readonly FluidStorageKey PLASMA =
		new("plasma", FluidState.PLASMA,
			m => $"{m.Id}_plasma",
			m => "gtceu.fluid.plasma",
			iconType: "plasma",
			registrationPriority: -1);

	public static readonly FluidStorageKey MOLTEN =
		new("molten", FluidState.LIQUID,
			m => $"molten_{m.Id}",
			m => "gtceu.fluid.molten",
			iconType: "molten",
			registrationPriority: -1);

	// Verbatim port of FluidStorageKeys.prefixedRegisteredName - the bare
	// material name when this key is the material's primary fluid form,
	// otherwise `<prefix><name>`.
	private static string PrefixedRegisteredName(string prefix, FluidStorageKey key, Material material)
	{
		var property = material.FluidProperty;
		if (property is not null && property.PrimaryKey != key)
			return prefix + material.Id;
		return material.Id;
	}

	private static string PostfixedRegisteredName(string postfix, FluidStorageKey key, Material material)
	{
		var property = material.FluidProperty;
		if (property is not null && property.PrimaryKey != key)
			return material.Id + postfix;
		return material.Id;
	}
}
