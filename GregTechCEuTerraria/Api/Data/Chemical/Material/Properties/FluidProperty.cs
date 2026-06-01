#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Fluids.Store;

namespace GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;

// Port of upstream com.gregtechceu.gtceu.api.data.chemical.material
// .properties.FluidProperty + FluidStorage/FluidStorageImpl, collapsed into
// one class (the upstream split exists only for the FluidStorage interface,
// which nothing else implements for us).
//
// A material's fluid registration state. FluidBuilders are ENQUEUED at
// material-load time (MaterialJsonLoader, from the JSON `fluids` array -
// mirrors upstream Material.Builder.fluid()/liquid()/gas()/plasma()), then
// built + registered in one pass by RegisterFluids (mirrors upstream
// FluidStorageImpl.registerFluids, driven from FluidLoader at Mod.Load).
public sealed class FluidProperty
{
	// Queued builders, before RegisterFluids runs. Preserves enqueue order so
	// PrimaryKey = first key declared.
	private readonly Dictionary<FluidStorageKey, FluidBuilder> _toRegister = new();
	// Built fluids, after RegisterFluids.
	private readonly Dictionary<FluidStorageKey, FluidType> _byKey = new();
	private bool _registered;
	private FluidType? _solidifyingFluid;

	// First key enqueued - the material's "default" fluid form. Drives the
	// LIQUID/GAS bare-vs-prefixed registry name (see FluidStorageKey).
	public FluidStorageKey? PrimaryKey { get; private set; }

	// === Enqueue (material-load time) =======================================

	// Verbatim port of FluidStorageImpl.enqueueRegistration.
	public void EnqueueRegistration(FluidStorageKey key, FluidBuilder builder)
	{
		if (_registered)
			throw new InvalidOperationException("Cannot enqueue a builder after registration");
		if (_toRegister.ContainsKey(key))
			throw new ArgumentException($"FluidStorageKey {key.Id} is already queued");
		_toRegister[key] = builder;
		PrimaryKey ??= key;
	}

	// The queued builder for a key - used by FluidBuilder's plasma-temperature
	// inference (reads the primary fluid's builder). Verbatim getQueuedBuilder.
	public FluidBuilder? GetQueuedBuilder(FluidStorageKey key) => _toRegister.GetValueOrDefault(key);

	// === Build + register (Mod.Load) ========================================

	// Verbatim port of FluidStorageImpl.registerFluids. Builds every queued
	// FluidBuilder in registration-priority order and installs the resulting
	// FluidType into FluidRegistry. Idempotent.
	public void RegisterFluids(Material material)
	{
		if (_registered) return;

		// Nothing queued and nothing stored - give the material a default
		// LIQUID so a material with a FLUID property always has a fluid.
		if (_toRegister.Count == 0 && _byKey.Count == 0)
			EnqueueRegistration(FluidStorageKey.LIQUID, new FluidBuilder().State(FluidState.LIQUID));

		foreach (var entry in _toRegister.OrderBy(e => -e.Key.RegistrationPriority))
		{
			var key = entry.Key;
			if (_byKey.ContainsKey(key)) continue;

			string id = entry.Value.ResolveName(material, key);
			// Skip-if-exists keeps the four hard-coded built-ins (water / lava /
			// steam / distilled_water) and makes reloads idempotent. Documented
			// adaptation - upstream has no pre-registered fluids.
			if (FluidRegistry.Get(id) is { } existing)
			{
				_byKey[key] = existing;
				continue;
			}
			var fluid = entry.Value.Build(material, key);
			FluidRegistry.Register(fluid);
			_byKey[key] = fluid;
		}
		_registered = true;
	}

	// === Accessors ==========================================================

	public FluidType? Get(FluidStorageKey key) => _byKey.GetValueOrDefault(key);
	public FluidType? Get() => PrimaryKey is null ? null : _byKey.GetValueOrDefault(PrimaryKey);

	public IEnumerable<FluidStorageKey> Keys => _byKey.Keys;
	public IEnumerable<FluidType> Fluids => _byKey.Values;

	// Verbatim port of FluidProperty.solidifiesFrom - the fluid that solidifies
	// into this material; defaults to the LIQUID-key fluid.
	public void SetSolidifyingFluid(FluidType? fluid) => _solidifyingFluid = fluid;

	public FluidType? SolidifiesFrom()
	{
		_solidifyingFluid ??= _byKey.GetValueOrDefault(FluidStorageKey.LIQUID);
		return _solidifyingFluid;
	}
}
