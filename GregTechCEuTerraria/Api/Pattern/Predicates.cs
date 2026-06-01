#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Machine.Multiblock;
using GregTechCEuTerraria.Api.Pattern.Error;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles;
using Terraria;

namespace GregTechCEuTerraria.Api.Pattern;

// Port of com.gregtechceu.gtceu.api.pattern.Predicates.
//
// Factory class for the standard pattern predicates referenced from each
// multiblock's registration row (`.where('S', controller(blocks(...)))`,
// `.where('X', heatingCoils())`, etc.). Each factory builds a
// `TraceabilityPredicate` wrapping a `SimplePredicate`.
//
// === Coverage ===============================================================
// Ported (cleanly translates to 2D / Terraria tiles):
//    Controller, Blocks(ushort...), Machines(MachineDefinition...), Custom,
//    Any, Air, Abilities, Ability, AutoAbilities (x3 overloads), Frames,
//    DataHatchPredicate, HeatingCoils, CleanroomFilters.
//
// Skipped (Forge-typed or deferred until adjacent subsystem lands):
//    States(BlockState...)        - Forge-only
//    BlockTag/FluidTag/Fluids     - Forge tag/fluid system
//    Lamps / AnyLamp / LampsByColor - lamp block registry
//    PowerSubstationBatteries     - port when substation lands
//
// Documented adaptations:
//   - `Block...` -> `ushort...` (Terraria tile types).
//   - `MetaMachineBlock` -> drop; `Machines(...)` reads our `MachineDefinition`
//     directly, looking up each definition's registered tile type(s).
//   - `Predicate<MultiblockState>` -> `Func<MultiblockState, bool>`.
//   - `Supplier<BlockInfo[]>` -> `Func<Item[]>`.
//   - `Component...` tooltips -> `string...`.
//   - `autoAbilities` gates each hatch type on `recipeType.HasInput(cap)`
//     / `HasOutput(cap)` - derived lazily from the loaded recipe set
//     (`GTRecipeType.HasInput`/`HasOutput`) since our recipe builder DSL
//     doesn't carry configured per-cap slot counts. Behaviour mirrors
//     upstream's `getMaxInputs(cap) > 0` check verbatim - a generator
//     multi (no EU input recipes) skips INPUT_ENERGY; a consumer multi
//     skips OUTPUT_ENERGY; a multi with no fluid recipes skips fluid hatches.
//   - `heatingCoils()` / `cleanroomFilters()` rely on registries that aren't
//     ported yet (`HEATING_COILS`, `CLEANROOM_FILTERS`). Both are stubbed
//     to "no candidates, never matches" so registration code references
//     resolve; populate once the registries land.
public static class Predicates
{
	// Mark a predicate as the controller cell. Set once per pattern.
	public static TraceabilityPredicate Controller(TraceabilityPredicate predicate) =>
		predicate.SetController();

	// Controller cell - auto-resolves the multi's controller tile from its
	// definition. Eliminates the `Controller(Blocks(ResolveTileType(<id>) ?? 0))`
	// ritual every multi factory used to write - drift / typo surface gone.
	// Reads every per-tier tile the definition has registered, so a tiered
	// multi (none today, but the surface is uniform) is handled transparently.
	public static TraceabilityPredicate Controller(MachineDefinition def) =>
		Machines(def).SetController();

	// Matches any of the given Terraria tile types at the current cell anchor.
	public static TraceabilityPredicate Blocks(params ushort[] tileTypes) =>
		new(new PredicateBlocks(tileTypes));

	// Name-based overload - resolves each tile name via the mod's ModTile
	// registry and logs a warning if any name is missing. Single point that
	// catches typos in pattern factories. Returns a predicate over the names
	// that DID resolve; an all-missing pattern logs once and matches nothing.
	public static TraceabilityPredicate Blocks(params string[] tileNames)
	{
		var types = new List<ushort>(tileNames.Length);
		var mod = Terraria.ModLoader.ModLoader.GetMod("GregTechCEuTerraria");
		foreach (var name in tileNames)
		{
			if (string.IsNullOrEmpty(name)) continue;
			if (mod.TryFind<Terraria.ModLoader.ModTile>(name, out var tile))
				types.Add((ushort)tile.Type);
			else
				mod.Logger.Warn($"Predicates.Blocks: tile name '{name}' did not resolve - pattern will not match it.");
		}
		return new(new PredicateBlocks(types.ToArray()));
	}

	// Matches the controller TILE TYPE of each MachineDefinition's registered
	// machine. Each definition can carry multiple per-tier tile types - we
	// include all of them, mirroring `MachineDefinition.get()` upstream.
	public static TraceabilityPredicate Machines(params MachineDefinition[] definitions)
	{
		var types = new List<ushort>();
		foreach (var def in definitions)
		{
			if (def is null) continue;
			foreach (var t in MachineRegistry.TilesForId(def.Id)) types.Add((ushort)t);
		}
		return Blocks(types.ToArray());
	}

	public static TraceabilityPredicate Custom(Func<MultiblockState, bool> predicate, Func<Item[]> candidates) =>
		new(predicate, candidates);

	public static TraceabilityPredicate Any() => new(SimplePredicate.ANY);

	public static TraceabilityPredicate Air() => new(SimplePredicate.AIR);

	// Matches if the cell's tile is registered against ANY of the given
	// PartAbility tokens (at any tier).
	public static TraceabilityPredicate Abilities(params PartAbility[] abilities)
	{
		var types = abilities.SelectMany(a => a.GetAllTiles()).Distinct().ToArray();
		return Blocks(types);
	}

	// Tier-restricted variant - if `tiers` is empty, all tiers match.
	public static TraceabilityPredicate Ability(PartAbility ability, params int[] tiers)
	{
		var types = (tiers.Length == 0 ? ability.GetAllTiles() : ability.GetTiles(tiers)).ToArray();
		return Blocks(types);
	}

	// Convenience: every standard hatch ability (every direction x every cap).
	public static TraceabilityPredicate AutoAbilities(params GTRecipeType[] recipeType) =>
		AutoAbilities(recipeType, true, true, true, true, true, true);

	// Full per-capability switch - verbatim port of upstream `autoAbilities(
	// GTRecipeType[], boolean, boolean, boolean, boolean, boolean, boolean)`.
	// Each hatch ability is emitted ONLY IF its `checkX` flag is on AND at
	// least one of the supplied recipe types has matching capability content
	// (`GTRecipeType.HasInput/HasOutput`). Upstream gates on
	// `type.getMaxInputs(cap) > 0`; our `GTRecipeType` derives that lazily
	// from the loaded recipe set.
	public static TraceabilityPredicate AutoAbilities(
		GTRecipeType[] recipeType,
		bool checkEnergyIn,
		bool checkEnergyOut,
		bool checkItemIn,
		bool checkItemOut,
		bool checkFluidIn,
		bool checkFluidOut)
	{
		TraceabilityPredicate predicate = new();

		bool AnyHasInput(object cap)
		{
			foreach (var t in recipeType) if (t.HasInput(cap)) return true;
			return false;
		}
		bool AnyHasOutput(object cap)
		{
			foreach (var t in recipeType) if (t.HasOutput(cap)) return true;
			return false;
		}

		if (checkEnergyIn  && AnyHasInput(Api.Capability.Recipe.EURecipeCapability.CAP))
			predicate = predicate.Or(Abilities(PartAbility.INPUT_ENERGY).SetMinGlobalLimited(1).SetMaxGlobalLimited(2).SetPreviewCount(1));
		if (checkEnergyOut && AnyHasOutput(Api.Capability.Recipe.EURecipeCapability.CAP))
			predicate = predicate.Or(Abilities(PartAbility.OUTPUT_ENERGY).SetMinGlobalLimited(1).SetMaxGlobalLimited(2).SetPreviewCount(1));
		if (checkItemIn    && AnyHasInput(Api.Capability.Recipe.ItemRecipeCapability.CAP))
			predicate = predicate.Or(Abilities(PartAbility.IMPORT_ITEMS).SetPreviewCount(1));
		if (checkItemOut   && AnyHasOutput(Api.Capability.Recipe.ItemRecipeCapability.CAP))
			predicate = predicate.Or(Abilities(PartAbility.EXPORT_ITEMS).SetPreviewCount(1));
		if (checkFluidIn   && AnyHasInput(Api.Capability.Recipe.FluidRecipeCapability.CAP))
			predicate = predicate.Or(Abilities(PartAbility.IMPORT_FLUIDS).SetPreviewCount(1));
		if (checkFluidOut  && AnyHasOutput(Api.Capability.Recipe.FluidRecipeCapability.CAP))
			predicate = predicate.Or(Abilities(PartAbility.EXPORT_FLUIDS).SetPreviewCount(1));
		return predicate;
	}

	// The canonical "wall cell" predicate every standard processing multi uses
	// upstream - a fixed-shape composition of:
	//   blocks(casing) [optional min]
	//     .or(autoAbilities(recipeTypes))           // recipe-driven I/O hatches
	//     .or(autoAbilities(maint, muffler, parallel))  // utility hatches
	// One declaration site for the whole pattern: ports of the ~20 standard
	// multis collapse to a single call per wall cell, with no opportunity for
	// the per-channel min/max numbers to drift across factories. Matches
	// upstream `GTMultiMachines.java` / `GCYMMachines.java` line-by-line -
	// upstream itself wraps the same three calls in every multi.
	//
	// **Documented adaptation** - upstream sets a `setMinGlobalLimited(N)` on the
	// casing branch (typical N = 14, 30, 50). Those counts are calibrated for
	// 3D cube shapes (60+ X cells); our 2D-collapsed shapes are 9-24 X cells,
	// so the same N silently blocks formation. Default here is 0 (no anti-
	// cheese guard) - physically there's no room for an all-hatch wall in our
	// shapes anyway. Pass `minGlobal: N` explicitly only when a specific shape
	// keeps enough room that a calibrated minimum still makes sense.
	public static TraceabilityPredicate StandardWall(
		string casingTileName,
		GTRecipeType[] recipeTypes,
		bool maintenance = true,
		bool muffler     = false,
		bool parallel    = false,
		int  minGlobal   = 0)
	{
		var casing = Blocks(casingTileName);
		if (minGlobal > 0) casing = casing.SetMinGlobalLimited(minGlobal);
		return casing
		    .Or(AutoAbilities(recipeTypes))
		    .Or(AutoAbilities(maintenance, muffler, parallel));
	}

	// Maintenance / muffler / parallel-hatch abilities - non-recipe-typed.
	// Upstream gates `maintenance` on a ConfigHolder; we always require >=1
	// when checkMaintenance is requested (port note: drop the gate; multis
	// that don't want it just don't call with checkMaintenance=true).
	public static TraceabilityPredicate AutoAbilities(
		bool checkMaintenance, bool checkMuffler, bool checkParallel)
	{
		TraceabilityPredicate predicate = new();
		if (checkMaintenance)
			predicate = predicate.Or(Abilities(PartAbility.MAINTENANCE).SetMinGlobalLimited(1).SetMaxGlobalLimited(1));
		if (checkMuffler)
			predicate = predicate.Or(Abilities(PartAbility.MUFFLER).SetMinGlobalLimited(1).SetMaxGlobalLimited(1));
		if (checkParallel)
			predicate = predicate.Or(Abilities(PartAbility.PARALLEL_HATCH).SetMaxGlobalLimited(1).SetPreviewCount(1));
		return predicate;
	}

	// Data-hatch (or grate) helper - used by research-station family. Upstream
	// gates on `ConfigHolder.machines.enableResearch`; we always require the
	// data hatch (no config system; multis that don't want research can omit
	// this predicate from their pattern).
	public static TraceabilityPredicate DataHatchPredicate(TraceabilityPredicate def) =>
		Abilities(PartAbility.DATA_ACCESS, PartAbility.OPTICAL_DATA_RECEPTION).SetExactLimit(1).Or(def);

	// Material frame tiles - `<material>_frame` per the `frame` MaterialPrefix.
	// Upstream also recognises framed pipes (a pipe block whose frame material
	// matches); pipes aren't placement-ported yet, so this is just the frame
	// tile for now.
	public static TraceabilityPredicate Frames(params Material[] frameMaterials)
	{
		var types = new List<ushort>();
		foreach (var m in frameMaterials)
		{
			if (m is null) continue;
			var t = MaterialItemRegistry.Get(m.Id, MaterialPrefixes.Frame.Id);
			if (t is null) continue;
			// MaterialItem.Type is the item type; the frame tile is registered
			// via MaterialBlockTileRegistry under the bare id `<material>_frame`.
			var tileType = MaterialBlockTileRegistry.Get($"{m.Id}_frame");
			if (tileType.HasValue) types.Add((ushort)tileType.Value);
		}
		return Blocks(types.ToArray());
	}

	// === STUBS ==============================================================
	// These reference registries we haven't ported yet. They register a
	// predicate that never matches so registration code can still construct
	// patterns referencing them; populate when the underlying registries
	// land.

	// Match a heating coil casing tile (any of the 8 upstream coil tiers) and
	// capture the resolved `Api.Block.CoilType` into the match context under
	// "CoilType". Mixed-tier rooms (e.g. some cupronickel + some kanthal)
	// reject - upstream behaviour, the room is one heat-tier.
	//
	// Shape mirrors `CleanroomFilters()` - same composition with the universal
	// `PredicateBlocks.CandidatesForTiles` resolver for ghost previews.
	public static TraceabilityPredicate HeatingCoils()
	{
		(ushort tileType, Api.Block.CoilType coil)[]? entries = null;

		bool MatchCell(MultiblockState state)
		{
			entries ??= ResolveCoilEntries();
			var tile = Main.tile[state.PosX, state.PosY];
			if (!tile.HasTile) return false;
			foreach (var (tileType, coil) in entries)
			{
				if (tile.TileType != tileType) continue;
				var existing = state.MatchContext.Get<Api.Block.CoilType>("CoilType");
				if (existing is null)
					state.MatchContext.Set("CoilType", coil);
				else if (!ReferenceEquals(existing, coil))
					return false;
				return true;
			}
			return false;
		}

		entries ??= ResolveCoilEntries();
		var tileTypes = entries.Length > 0
			? System.Array.ConvertAll(entries, e => e.tileType)
			: System.Array.Empty<ushort>();
		return new TraceabilityPredicate(MatchCell, PredicateBlocks.CandidatesForTiles(tileTypes));
	}

	private static (ushort tileType, Api.Block.CoilType coil)[] ResolveCoilEntries()
	{
		var mod = Terraria.ModLoader.ModLoader.GetMod("GregTechCEuTerraria");
		var list = new List<(ushort, Api.Block.CoilType)>();
		foreach (var c in Api.Block.CoilType.All)
		{
			if (mod.TryFind<Terraria.ModLoader.ModTile>(c.TileName, out var t))
				list.Add(((ushort)t.Type, c));
		}
		return list.ToArray();
	}

	// Match a cleanroom-filter casing tile (filter_casing or
	// sterilizing_filter_casing) and capture the resolved
	// `CleanroomFilterType` into the match context under key "FilterType" -
	// `CleanroomMachine.OnStructureFormed` reads it to set its CleanroomType.
	// Mixed filter types within one structure short-circuit to a failed match
	// (upstream behaviour - a room is either CLEANROOM or STERILE, not both).
	public static TraceabilityPredicate CleanroomFilters()
	{
		// Lazy tile-type resolution - CasingRegistry hasn't run when this
		// factory is invoked at registration time, so resolve on first match.
		(ushort tileType, Common.Block.CleanroomFilterType filter)[]? entries = null;

		bool MatchCell(MultiblockState state)
		{
			entries ??= ResolveFilterEntries();
			var tile = Main.tile[state.PosX, state.PosY];
			if (!tile.HasTile) return false;
			foreach (var (tileType, filter) in entries)
			{
				if (tile.TileType != tileType) continue;
				var existing = state.MatchContext.Get<Common.Block.CleanroomFilterType>("FilterType");
				if (existing is null)
					state.MatchContext.Set("FilterType", filter);
				else if (!ReferenceEquals(existing, filter))
					return false;
				return true;
			}
			return false;
		}

		// Candidates resolve through the universal tile-types -> items helper
		// (`PredicateBlocks.CandidatesForTiles`) so the preview renderer +
		// hover tooltip share the same path as `Predicates.Blocks(...)`. The
		// match logic stays custom because we also write the matched type
		// into MatchContext (for OnStructureFormed) AND reject mixed-type
		// rooms - a side-effect-bearing predicate that pure `Blocks(...)`
		// can't express.
		entries ??= ResolveFilterEntries();
		var tileTypes = entries.Length > 0
			? System.Array.ConvertAll(entries, e => e.tileType)
			: System.Array.Empty<ushort>();
		return new TraceabilityPredicate(MatchCell, PredicateBlocks.CandidatesForTiles(tileTypes));
	}

	private static (ushort tileType, Common.Block.CleanroomFilterType filter)[] ResolveFilterEntries()
	{
		var mod = Terraria.ModLoader.ModLoader.GetMod("GregTechCEuTerraria");
		var list = new List<(ushort, Common.Block.CleanroomFilterType)>();
		foreach (var f in Common.Block.CleanroomFilterType.All)
		{
			if (mod.TryFind<Terraria.ModLoader.ModTile>(f.TileName, out var t))
				list.Add(((ushort)t.Type, f));
		}
		return list.ToArray();
	}
}
