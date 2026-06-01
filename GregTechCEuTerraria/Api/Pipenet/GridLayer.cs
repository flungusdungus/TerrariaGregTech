#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Pipenet;

// Sparse 2D map of placed cells on a "wire-style" layer parallel to the tile
// grid. The layer does not occupy block space - cells can coexist with any
// tile or with empty air.
//
// Abstract base shared by the cable layer (energy net) and the item / fluid
// pipe layers. Subclasses parameterise the per-cell payload type and override
// Connects to define when two adjacent cells form a network link (same-tier
// for cables, same-kind + same-material for pipes).
//
// Pure - no Terraria deps. Save/load + render lives on a per-layer ModSystem.
public abstract class GridLayer<TCell> where TCell : struct
{
	private readonly Dictionary<(int x, int y), TCell> _cells = new();

	public int Count => _cells.Count;
	public IReadOnlyDictionary<(int x, int y), TCell> All => _cells;

	// Set whenever topology changes (Set / Remove / Clear that actually mutated).
	// The owning network system polls and rebuilds; it calls ClearDirty after.
	public bool IsDirty { get; private set; }
	public void ClearDirty() => IsDirty = false;

	public bool Has(int x, int y) => _cells.ContainsKey((x, y));

	public TCell? CellAt(int x, int y) =>
		_cells.TryGetValue((x, y), out var c) ? c : null;

	public void Set(int x, int y, TCell cell)
	{
		if (_cells.TryGetValue((x, y), out var existing) &&
			EqualityComparer<TCell>.Default.Equals(existing, cell)) return;
		_cells[(x, y)] = cell;
		IsDirty = true;
	}

	public bool Remove(int x, int y)
	{
		bool removed = _cells.Remove((x, y));
		if (removed) IsDirty = true;
		return removed;
	}

	public void Clear()
	{
		if (_cells.Count == 0) return;
		_cells.Clear();
		IsDirty = true;
	}

	// Whether two layer positions form a connection - used by both the network
	// flood and the render mask. Subclasses define the connectivity rule.
	public abstract bool Connects(int x1, int y1, int x2, int y2);

	// N=1, S=2, W=4, E=8 - combined into a 0..15 frame index. A cell only
	// draws an arm toward a neighbour it Connects to, so the visual matches
	// the actual electrically/logically-separate networks.
	public int ConnectionMask(int x, int y)
	{
		int mask = 0;
		if (Connects(x, y, x, y - 1)) mask |= 1;
		if (Connects(x, y, x, y + 1)) mask |= 2;
		if (Connects(x, y, x - 1, y)) mask |= 4;
		if (Connects(x, y, x + 1, y)) mask |= 8;
		return mask;
	}
}
