#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Cover;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// Verbatim port of ItemNetWalker. BFS from a source pipe, emits one
// ItemRoutePath per (pipe -> adjacent item handler) found; accumulates
// network-wide min transferRate + summed priority + per-side filter chain.
public sealed class ItemNetWalker : PipeNetWalker<ItemPipeCell, ItemPipeProperties, ItemPipeNet>
{
	public static List<ItemRoutePath>? CreateNetData(ItemPipeNet pipeNet, (int x, int y) sourcePipe, CoverSide sourceFacing)
	{
		if (!ItemPipeLayerSystem.Pipes.Has(sourcePipe.x, sourcePipe.y)) return null;
		try
		{
			var walker = new ItemNetWalker(pipeNet, sourcePipe, 1, new List<ItemRoutePath>(), null);
			walker._sourcePipe = sourcePipe;
			walker._facingToHandler = sourceFacing;
			walker.TraversePipeNet();
			return walker._inventories;
		}
		catch (System.Exception)
		{
			// Cache stays unpopulated; next insertion attempt re-tries.
			return null;
		}
	}

	private ItemPipeProperties? _minProperties;
	private readonly List<ItemRoutePath> _inventories;
	private readonly List<Func<Terraria.Item, bool>> _filters = new();
	private readonly Dictionary<IODirection, List<Func<Terraria.Item, bool>>> _nextFilters = new();
	private (int x, int y) _sourcePipe;
	private CoverSide _facingToHandler;
	private bool _isRestricted;

	private ItemNetWalker(
		ItemPipeNet net, (int x, int y) sourcePipe, int distance,
		List<ItemRoutePath> inventories, ItemPipeProperties? properties)
		: base(net, sourcePipe, distance)
	{
		_inventories = inventories;
		_minProperties = properties;
	}

	protected override PipeNetWalker<ItemPipeCell, ItemPipeProperties, ItemPipeNet> CreateSubWalker(
		ItemPipeNet pipeNet, IODirection facingToNextPos, (int x, int y) nextPos, int walkedBlocks)
	{
		var walker = new ItemNetWalker(pipeNet, nextPos, walkedBlocks, _inventories, _minProperties)
		{
			_facingToHandler = _facingToHandler,
			_sourcePipe      = _sourcePipe,
		};
		walker._filters.AddRange(_filters);
		if (_nextFilters.TryGetValue(facingToNextPos, out var moreFilters) && moreFilters.Count > 0)
			walker._filters.AddRange(moreFilters);
		return walker;
	}

	protected override bool TryGetCellAt((int x, int y) pos, out ItemPipeCell cell)
	{
		var c = ItemPipeLayerSystem.Pipes.CellAt(pos.x, pos.y);
		if (c is null) { cell = default; return false; }
		cell = c.Value;
		return true;
	}

	protected override void CheckPipe(ItemPipeCell pipeTile, (int x, int y) pos)
	{
		// Flush pending next-side filters; upstream's checkPipe runs after
		// IsValidPipe seeded them.
		foreach (var list in _nextFilters.Values)
			if (list.Count > 0) _filters.AddRange(list);
		_nextFilters.Clear();

		var pipeProps = new ItemPipeProperties(pipeTile.Priority, pipeTile.TransferRate);
		if (pipeTile.Restrictive) _isRestricted = true;
		if (_minProperties is null)
		{
			_minProperties = pipeProps;
		}
		else
		{
			_minProperties = new ItemPipeProperties(
				_minProperties.Priority + pipeProps.Priority,
				System.Math.Min(_minProperties.TransferRate, pipeProps.TransferRate));
		}
	}

	protected override void CheckNeighbour(
		ItemPipeCell pipeTile, (int x, int y) pipePos, IODirection faceToNeighbour, object? neighbourTile)
	{
		// Skip source side (that's the feeding handler).
		if (pipePos == _sourcePipe && ToCoverSide(faceToNeighbour) == _facingToHandler) return;

		// Routes emit only through Passive / Active sides; Off = not connected.
		var modeAtSide = ItemPipeLayerSystem.GetSides(pipePos.x, pipePos.y)
			?.GetMode(ToCoverSide(faceToNeighbour)) ?? PipeSideMode.Off;
		if (modeAtSide == PipeSideMode.Off) return;

		var (dx, dy) = OffsetForIODirection(faceToNeighbour);
		var arrivalSide = IODirectionOpposite(faceToNeighbour);
		var handler = WorldCapability.ItemHandlerAt(pipePos.x + dx, pipePos.y + dy, arrivalSide);
		if (handler is null) return;

		// Snapshot filters - the walker mutates _filters as it advances.
		var filtersForRoute = new List<Func<Terraria.Item, bool>>(_filters);
		if (_nextFilters.TryGetValue(faceToNeighbour, out var moreFilters) && moreFilters.Count > 0)
			filtersForRoute.AddRange(moreFilters);

		_inventories.Add(new ItemRoutePath(
			targetPipe: pipePos,
			facing:     ToCoverSide(faceToNeighbour),
			distance:   WalkedBlocks,
			properties: _minProperties ?? new ItemPipeProperties(pipeTile.Priority, pipeTile.TransferRate),
			restrictive: _isRestricted,
			filters:    filtersForRoute));
	}

	protected override bool IsValidPipe(
		ItemPipeCell currentPipe, ItemPipeCell neighbourPipe, (int x, int y) pipePos, IODirection faceToNeighbour)
	{
		// Read covers on BOTH sides of the connection; shutter blocks, filter
		// covers stack their .Test in the appropriate direction.
		var thisCover      = ItemPipeLayerSystem.GetSides(pipePos.x, pipePos.y)?
			.GetCoverAtSide(ToCoverSide(faceToNeighbour));
		var (nx, ny) = (pipePos.x + OffsetForIODirection(faceToNeighbour).dx,
		                pipePos.y + OffsetForIODirection(faceToNeighbour).dy);
		var neighbourCover = ItemPipeLayerSystem.GetSides(nx, ny)?
			.GetCoverAtSide(ToCoverSide(IODirectionOpposite(faceToNeighbour)));

		var collected = new List<Func<Terraria.Item, bool>>(2);
		switch (thisCover)
		{
			case ShutterCover shutter:
				collected.Add(_ => !shutter.IsWorkingEnabled());
				break;
			case ItemFilterCover filter when filter.FilterMode != FilterMode.FilterInsert:
				collected.Add(filter.GetItemFilter().Test);
				break;
		}
		switch (neighbourCover)
		{
			case ShutterCover shutter:
				collected.Add(_ => !shutter.IsWorkingEnabled());
				break;
			case ItemFilterCover filter when filter.FilterMode != FilterMode.FilterExtract:
				collected.Add(filter.GetItemFilter().Test);
				break;
		}
		if (collected.Count > 0) _nextFilters[faceToNeighbour] = collected;
		return true;
	}

	private static CoverSide ToCoverSide(IODirection dir) => dir switch
	{
		IODirection.Up    => CoverSide.Up,
		IODirection.Down  => CoverSide.Down,
		IODirection.Left  => CoverSide.Left,
		IODirection.Right => CoverSide.Right,
		_                 => CoverSide.Up,
	};

	private static IODirection IODirectionOpposite(IODirection d) => d switch
	{
		IODirection.Up    => IODirection.Down,
		IODirection.Down  => IODirection.Up,
		IODirection.Left  => IODirection.Right,
		IODirection.Right => IODirection.Left,
		_                 => IODirection.None,
	};

	private static (int dx, int dy) OffsetForIODirection(IODirection dir) => dir switch
	{
		IODirection.Up    => (0, -1),
		IODirection.Down  => (0, +1),
		IODirection.Left  => (-1, 0),
		IODirection.Right => (+1, 0),
		_                 => (0, 0),
	};
}
