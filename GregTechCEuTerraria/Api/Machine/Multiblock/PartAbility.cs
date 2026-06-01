#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace GregTechCEuTerraria.Api.Machine.Multiblock;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.machine.multiblock.PartAbility.
//
// A named ability token (32 of them) that a part-machine declares it provides
// to a multiblock controller - item import/export, fluid import/export (with
// 1x/4x/9x size variants), energy in/out, steam, maintenance, muffler, parallel
// hatch, laser, rotor holder, etc. The controller, when scanning a structure,
// asks "which tiles in my footprint match ability X?" via `IsApplicable`, and
// binds them as part inputs/outputs to its `RecipeLogic`.
//
// Adaptations:
//   - `Block` -> `ushort` tile type. Upstream maps tier -> Set<Block>; we map
//     tier -> Set<ushort>. Tile types are what we read from `Main.tile[i,j]`
//     when matching, so the registry holds the same kind of identifier the
//     matcher will compare against.
//   - `Int2ObjectMap<Set<Block>>` -> `Dictionary<int, HashSet<ushort>>`.
//   - `GTMemoizer.memoize(...)` -> a manually-flushed cache field. Register
//     calls invalidate it.
//   - `Iterable<Block>` / `Collection<Block>` accessors return `IReadOnlyList`s.
//
// The 32 static constants ARE included verbatim - they're just identifiers, so
// even the not-yet-ported abilities (HPCA, optical data, computation) carry no
// cost beyond the name string until something registers a tile against them.
public class PartAbility
{
	public static readonly PartAbility EXPORT_ITEMS  = new("export_items");
	public static readonly PartAbility IMPORT_ITEMS  = new("import_items");
	public static readonly PartAbility EXPORT_FLUIDS = new("export_fluids");
	public static readonly PartAbility IMPORT_FLUIDS = new("import_fluids");

	public static readonly PartAbility EXPORT_FLUIDS_1X = new("export_fluids_1x");
	public static readonly PartAbility IMPORT_FLUIDS_1X = new("import_fluids_1x");
	public static readonly PartAbility EXPORT_FLUIDS_4X = new("export_fluids_4x");
	public static readonly PartAbility IMPORT_FLUIDS_4X = new("import_fluids_4x");
	public static readonly PartAbility EXPORT_FLUIDS_9X = new("export_fluids_9x");
	public static readonly PartAbility IMPORT_FLUIDS_9X = new("import_fluids_9x");

	public static readonly PartAbility INPUT_ENERGY              = new("input_energy");
	public static readonly PartAbility OUTPUT_ENERGY             = new("output_energy");
	public static readonly PartAbility SUBSTATION_INPUT_ENERGY   = new("substation_input_energy");
	public static readonly PartAbility SUBSTATION_OUTPUT_ENERGY  = new("substation_output_energy");
	public static readonly PartAbility ROTOR_HOLDER              = new("rotor_holder");
	public static readonly PartAbility PUMP_FLUID_HATCH          = new("pump_fluid_hatch");
	public static readonly PartAbility STEAM                     = new("steam");
	public static readonly PartAbility STEAM_IMPORT_ITEMS        = new("steam_import_items");
	public static readonly PartAbility STEAM_EXPORT_ITEMS        = new("steam_export_items");
	public static readonly PartAbility MAINTENANCE               = new("maintenance");
	public static readonly PartAbility MUFFLER                   = new("muffler");
	public static readonly PartAbility TANK_VALVE                = new("tank_valve");
	public static readonly PartAbility PASSTHROUGH_HATCH         = new("passthrough_hatch");
	public static readonly PartAbility PARALLEL_HATCH            = new("parallel_hatch");
	public static readonly PartAbility INPUT_LASER               = new("input_laser");
	public static readonly PartAbility OUTPUT_LASER              = new("output_laser");

	public static readonly PartAbility COMPUTATION_DATA_RECEPTION    = new("computation_data_reception");
	public static readonly PartAbility COMPUTATION_DATA_TRANSMISSION = new("computation_data_transmission");
	public static readonly PartAbility OPTICAL_DATA_RECEPTION        = new("optical_data_reception");
	public static readonly PartAbility OPTICAL_DATA_TRANSMISSION     = new("optical_data_transmission");

	public static readonly PartAbility DATA_ACCESS = new("data_access");

	public static readonly PartAbility HPCA_COMPONENT = new("hpca_component");
	public static readonly PartAbility OBJECT_HOLDER  = new("object_holder");

	// tier -> available tile types
	private readonly Dictionary<int, HashSet<ushort>> _registry = new();

	// Cache of every tile type across every tier - invalidated on Register.
	// Mirrors upstream's GTMemoizer.memoize call.
	private IReadOnlyList<ushort>? _allTilesCache;

	public string Name { get; }

	public PartAbility(string name)
	{
		Name = name;
	}

	public void Register(int tier, ushort tileType)
	{
		if (!_registry.TryGetValue(tier, out var set))
		{
			set = new HashSet<ushort>();
			_registry[tier] = set;
		}
		set.Add(tileType);
		_allTilesCache = null;
	}

	public IReadOnlyList<ushort> GetAllTiles()
	{
		return _allTilesCache ??= _registry.Values.SelectMany(s => s).Distinct().ToList();
	}

	public bool IsApplicable(ushort tileType) => GetAllTiles().Contains(tileType);

	public IReadOnlyList<ushort> GetTiles(params int[] tiers)
	{
		return _registry
			.Where(kv => tiers.Contains(kv.Key))
			.SelectMany(kv => kv.Value)
			.ToList();
	}

	// Inclusive range [from, to].
	public IReadOnlyList<ushort> GetTileRange(int from, int to)
	{
		return _registry
			.Where(kv => kv.Key >= from && kv.Key <= to)
			.SelectMany(kv => kv.Value)
			.ToList();
	}
}
