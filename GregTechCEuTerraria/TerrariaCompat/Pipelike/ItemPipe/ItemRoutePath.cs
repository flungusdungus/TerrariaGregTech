#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// Verbatim port of ItemRoutePath. Immutable snapshot populated by
// ItemNetWalker; used by ItemNetHandler's distribution modes.
public sealed class ItemRoutePath
{
	public (int X, int Y) TargetPipe   { get; }
	public CoverSide TargetFacing       { get; }
	public int Distance                 { get; }
	public ItemPipeProperties Properties { get; }
	public bool Restrictive             { get; }

	private readonly Func<Terraria.Item, bool> _filters;

	public ItemRoutePath(
		(int X, int Y) targetPipe, CoverSide facing, int distance,
		ItemPipeProperties properties, bool restrictive,
		IReadOnlyList<Func<Terraria.Item, bool>> filters)
	{
		TargetPipe = targetPipe;
		TargetFacing = facing;
		Distance = distance;
		Properties = properties;
		Restrictive = restrictive;
		// Composed - one virtual call per insertion regardless of cover count.
		_filters = stack =>
		{
			for (int i = 0; i < filters.Count; i++)
				if (!filters[i](stack)) return false;
			return true;
		};
	}

	public (int X, int Y) TargetPipePos => TargetPipe;

	public IItemHandler? GetHandler()
	{
		var dir = ToIODirection(TargetFacing);
		var (dx, dy) = OffsetForCoverSide(TargetFacing);
		var arrivalSide = IODirectionOpposite(dir);
		return WorldCapability.ItemHandlerAt(TargetPipe.X + dx, TargetPipe.Y + dy, arrivalSide);
	}

	public bool MatchesFilters(Terraria.Item stack) => _filters(stack);

	public (int X, int Y, CoverSide F) ToFacingPos() => (TargetPipe.X, TargetPipe.Y, TargetFacing);

	private static IODirection ToIODirection(CoverSide side) => side switch
	{
		CoverSide.Up    => IODirection.Up,
		CoverSide.Down  => IODirection.Down,
		CoverSide.Left  => IODirection.Left,
		CoverSide.Right => IODirection.Right,
		_               => IODirection.None,
	};

	private static IODirection IODirectionOpposite(IODirection d) => d switch
	{
		IODirection.Up    => IODirection.Down,
		IODirection.Down  => IODirection.Up,
		IODirection.Left  => IODirection.Right,
		IODirection.Right => IODirection.Left,
		_                 => IODirection.None,
	};

	private static (int dx, int dy) OffsetForCoverSide(CoverSide side) => side switch
	{
		CoverSide.Up    => (0, -1),
		CoverSide.Down  => (0, +1),
		CoverSide.Left  => (-1, 0),
		CoverSide.Right => (+1, 0),
		_               => (0, 0),
	};
}
