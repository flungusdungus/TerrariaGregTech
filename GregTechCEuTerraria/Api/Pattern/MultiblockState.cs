#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Pattern.Error;

using GregTechCEuTerraria.Api.Pattern.Util;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.Api.Pattern;

// Port of com.gregtechceu.gtceu.api.pattern.MultiblockState.
//
// Per-tick state passed to every predicate while a multiblock controller scans
// its surroundings. Carries: the tile being tested, the active predicate at
// that cell, accumulated counts across the structure, an error to report on
// failure, and a shared `PatternMatchContext` for cross-predicate state.
//
// === 2D + 2x2-tile coordinate convention ====================================
//
// Every gtceu "block" maps to a 2x2 footprint of Terraria tiles. Coordinates
// in this class are ALWAYS tile-space anchors - i.e. the top-left tile of a
// 2x2 block's footprint. When the matcher iterates a `string[]` shape grid:
//
//     state.Update(controllerAnchor + (col*2, row*2), predicateForChar)
//
// The +2-tile step per shape-cell is the matcher's job; predicates that read
// the world read tiles directly at `state.PosX` / `state.PosY` (the anchor
// tile), via `Main.tile[...]` or `MetaMachine.GetMachineAt(anchorX, anchorY)`.
//
// Documented adaptations:
//   - `BlockPos` -> `(int X, int Y)` tile-space anchor (2D - no Z).
//   - `Level world` reference dropped; Terraria is a single-world game, so
//     tile reads use `Main.tile[]` directly. `world.isLoaded(pos)` always
//     true in single-player (only matters for unloaded chunks).
//   - `BlockState` -> `ushort` tile type (`Main.tile[x,y].TileType`).
//   - `BlockEntity` -> `MetaMachine?` (the machine pinned at the anchor tile,
//     or null if the cell isn't a machine).
//   - `Direction face` (used by `getOffsetState`) -> drop; the matcher walks
//     a flat 2D grid, no per-face offset needed.
//   - `LongOpenHashSet cache` -> `HashSet<long>` with packed `(x << 32) | y`
//     entries; matches upstream's `pos.asLong()` packing semantically.
//   - `MultiblockWorldSavedData` reference dropped - we save controller state
//     in-tile-entity (`MetaMachine.NetSend`/`NetReceive`) rather than as a
//     world-saved-data blob. `onBlockStateChanged` consequently simplified.
//   - `ActiveBlock`/`Direction face` rendering hint dropped (no analog).
//   - `setFlipped` / `neededFlip` retained - even in 2D the matcher may need
//     to try the shape mirrored horizontally.
public class MultiblockState
{
	public static readonly PatternError UNLOAD_ERROR = new PatternStringError("multiblocked.pattern.error.chunk");
	public static readonly PatternError UNINIT_ERROR = new PatternStringError("multiblocked.pattern.error.init");

	// Current tile being tested (top-left anchor of a 2x2 block).
	public int PosX;
	public int PosY;

	// Lazy reads of the tile at (PosX, PosY). The matcher resets them per cell
	// via Update(); predicates fetch on demand.
	private ushort? _tileType;
	private TerrariaCompat.Machine.MetaMachine? _machine;
	private bool _machineProbed;

	public PatternMatchContext MatchContext { get; }

	// Per-predicate global-count map (predicate -> number of times it matched
	// across the structure). Used by predicate.setMinGlobalLimited / etc.
	public Dictionary<SimplePredicate, int> GlobalCount = new();
	public Dictionary<SimplePredicate, int> LayerCount  = new();

	public TraceabilityPredicate? Predicate;
	public IO Io;
	public PatternError? Error;
	public bool NeededFlip;

	// Anchor tile of the controller (the 2x2 block the player right-clicks
	// to bring up the multi GUI). All matcher iteration is relative to this.
	public readonly int ControllerPosX;
	public readonly int ControllerPosY;
	public MultiblockControllerMachine? LastController;

	// Tile positions visited during the last successful match, used so the
	// controller can invalidate when any of them changes. Packed `(x << 32) | y`.
	public HashSet<long>? Cache;

	public MultiblockState(int controllerPosX, int controllerPosY)
	{
		ControllerPosX = controllerPosX;
		ControllerPosY = controllerPosY;
		Error = UNINIT_ERROR;
		MatchContext = new PatternMatchContext();
	}

	public void Clean()
	{
		MatchContext.Reset();
		GlobalCount = new Dictionary<SimplePredicate, int>();
		LayerCount  = new Dictionary<SimplePredicate, int>();
		Cache = new HashSet<long>();
	}

	// Move to the next cell. Returns false if the world hasn't loaded the
	// tile (chunk-unloaded equivalent - in Terraria, Main.tile may be null
	// outside world bounds).
	public bool Update(int posX, int posY, TraceabilityPredicate predicate)
	{
		PosX = posX;
		PosY = posY;
		_tileType = null;
		_machine = null;
		_machineProbed = false;
		Predicate = predicate;
		Error = null;
		if (!IsTileLoaded(posX, posY))
		{
			Error = UNLOAD_ERROR;
			return false;
		}
		return true;
	}

	private static bool IsTileLoaded(int x, int y) =>
		x >= 0 && y >= 0 && x < Terraria.Main.maxTilesX && y < Terraria.Main.maxTilesY;

	public MultiblockControllerMachine? GetController()
	{
		if (!IsTileLoaded(ControllerPosX, ControllerPosY))
		{
			Error = UNLOAD_ERROR;
			return null;
		}
		var machine = TerrariaCompat.Machine.MetaMachine.GetMachineAt(ControllerPosX, ControllerPosY);
		if (machine is MultiblockControllerMachine controller)
		{
			LastController = controller;
			return controller;
		}
		return null;
	}

	public bool HasError() => Error != null;

	public void SetError(PatternError? error)
	{
		Error = error;
		if (error != null)
			error.SetWorldState(this);
	}

	// Tile-type at the current cell's anchor. Lazy + cached for the cell.
	public ushort GetTileType()
	{
		if (_tileType is null)
		{
			var tile = Terraria.Main.tile[PosX, PosY];
			_tileType = tile.HasTile ? tile.TileType : (ushort)0;
		}
		return _tileType.Value;
	}

	// MetaMachine occupying the current cell, or null if none. Lazy.
	//
	// Walks back to the entity anchor via tile.TileFrame instead of probing
	// the bare PosX/PosY: the pattern walker visits the top-left tile of each
	// 2-tile shape cell, but a 2x2 machine's anchor may sit at any of the
	// cell's 4 sub-tiles depending on placement parity vs the controller's
	// origin. The bare lookup misses 3 of 4 sub-tile positions, so off-grid
	// parts never bound to their controller and interior machines were
	// invisible to custom predicates (e.g. CleanroomMachine.InnerPredicateMatch).
	// MachineCellResolver.TryFindMachineAt is the canonical "machine occupying
	// this tile" probe used everywhere else in the codebase.
	public TerrariaCompat.Machine.MetaMachine? GetMachine()
	{
		if (!_machineProbed)
		{
			_machine = TerrariaCompat.Machine.MachineCellResolver
				.TryFindMachineAt(PosX, PosY, out var m) ? m : null;
			_machineProbed = true;
		}
		return _machine;
	}

	public (int X, int Y) GetPos() => (PosX, PosY);

	public void AddPosCache(int x, int y) => Cache?.Add(PackPos(x, y));
	public bool IsPosInCache(int x, int y) => Cache?.Contains(PackPos(x, y)) ?? false;

	public IEnumerable<(int X, int Y)> GetCache()
	{
		if (Cache is null) yield break;
		foreach (var packed in Cache)
			yield return UnpackPos(packed);
	}

	public static long PackPos(int x, int y) => ((long)x << 32) | (uint)y;
	public static (int X, int Y) UnpackPos(long packed) => ((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
}
