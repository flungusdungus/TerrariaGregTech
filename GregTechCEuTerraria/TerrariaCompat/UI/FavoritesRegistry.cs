#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Per-character favorites (items + fluids pinned via Alt-click in the browser).
// List, not HashSet, to preserve insertion order for the pane layout. Each entry
// is tagged: ItemType > 0 = item, non-null FluidId = fluid (exactly one set).
// Persisted per-player via FavoritesPlayer; item entries round-trip through
// ItemIO so a mod removal drops the favorite instead of resolving to a stale id.
public static class FavoritesRegistry
{
	public readonly record struct Entry(int ItemType, string? FluidId, string? FluidLabel);

	private static readonly List<Entry> _entries = new();

	public static IReadOnlyList<Entry> Entries => _entries;

	public static bool IsItemFavorite(int itemType) =>
		itemType > 0 && IndexOfItem(itemType) >= 0;

	public static bool IsFluidFavorite(string fluidId) =>
		!string.IsNullOrEmpty(fluidId) && IndexOfFluid(fluidId) >= 0;

	// Add at top, or move-to-top if already present (newest pin most visible).
	public static void BringItemToFront(int itemType)
	{
		if (itemType <= 0) return;
		int idx = IndexOfItem(itemType);
		var entry = idx >= 0 ? _entries[idx] : new Entry(itemType, null, null);
		if (idx >= 0) _entries.RemoveAt(idx);
		_entries.Insert(0, entry);
	}

	public static void BringFluidToFront(string fluidId, string? fluidLabel)
	{
		if (string.IsNullOrEmpty(fluidId)) return;
		int idx = IndexOfFluid(fluidId);
		var entry = idx >= 0 ? _entries[idx] : new Entry(0, fluidId, fluidLabel);
		if (idx >= 0) _entries.RemoveAt(idx);
		_entries.Insert(0, entry);
	}

	// Unconditional remove - the favorites-pane Alt+LMB path. No-op if absent.
	public static void RemoveItem(int itemType)
	{
		if (itemType <= 0) return;
		int idx = IndexOfItem(itemType);
		if (idx >= 0) _entries.RemoveAt(idx);
	}

	public static void RemoveFluid(string fluidId)
	{
		if (string.IsNullOrEmpty(fluidId)) return;
		int idx = IndexOfFluid(fluidId);
		if (idx >= 0) _entries.RemoveAt(idx);
	}

	public static void Clear() => _entries.Clear();

	// Idempotent append for FavoritesPlayer.LoadData (Toggle would remove a
	// present entry mid-restore).
	internal static void AddItemSilent(int itemType)
	{
		if (itemType <= 0 || IndexOfItem(itemType) >= 0) return;
		_entries.Add(new Entry(itemType, null, null));
	}

	internal static void AddFluidSilent(string fluidId, string? fluidLabel)
	{
		if (string.IsNullOrEmpty(fluidId) || IndexOfFluid(fluidId) >= 0) return;
		_entries.Add(new Entry(0, fluidId, fluidLabel));
	}

	private static int IndexOfItem(int itemType)
	{
		for (int i = 0; i < _entries.Count; i++)
			if (_entries[i].ItemType == itemType) return i;
		return -1;
	}

	private static int IndexOfFluid(string fluidId)
	{
		for (int i = 0; i < _entries.Count; i++)
			if (_entries[i].FluidId == fluidId) return i;
		return -1;
	}
}
