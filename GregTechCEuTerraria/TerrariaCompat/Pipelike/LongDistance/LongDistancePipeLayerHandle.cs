#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// LD-pipe place/cut/refund cycle. One handle instance per pipe Type (item /
// fluid) so the refund spawns the right pipe item; both write into the single
// shared LongDistancePipeLayer with the cell's Type tag. Parallel to
// LaserPipeLayerHandle.
public sealed class LongDistancePipeLayerHandle : IGridLayerHandle
{
	public static readonly LongDistancePipeLayerHandle Item  =
		new(LongDistancePipeType.Item,  "long_distance_item_pipeline");
	public static readonly LongDistancePipeLayerHandle Fluid =
		new(LongDistancePipeType.Fluid, "long_distance_fluid_pipeline");

	private readonly LongDistancePipeType _type;
	private readonly string _itemName;

	private LongDistancePipeLayerHandle(LongDistancePipeType type, string itemName)
	{
		_type = type;
		_itemName = itemName;
	}

	// Any LD cell present (either type). Used by the held-item cut path to know
	// whether THIS cell exists; CutAt then refunds the matching item.
	public bool Has(int x, int y) => LongDistancePipeLayerSystem.Pipes.Has(x, y);

	public bool TryPlace(int x, int y, Player placer)
	{
		if (LongDistancePipeLayerSystem.Pipes.Has(x, y)) return false;
		LongDistancePipeLayerSystem.Pipes.Set(x, y, new LongDistancePipeCell(_type));
		LongDistancePipeNetSystem.OnPipeAdded(x, y);
		// MP authority: client mutates locally + ships placement; server applies
		// + echoes to other clients. Mirrors the laser/optical place path.
		PipePackets.SendPlacedLongDistance(x, y, _type);
		return true;
	}

	public bool CutAt(int x, int y, Player remover)
	{
		var cell = LongDistancePipeLayerSystem.Pipes.CellAt(x, y);
		if (cell is null) return false;
		LongDistancePipeLayerSystem.Pipes.Remove(x, y);
		LongDistancePipeNetSystem.OnPipeRemoved(x, y);
		PipePackets.SendRemove(x, y, PipeKind.LongDistance);
		// Refund the item matching the REMOVED cell's type, not this handle's.
		string itemName = cell.Value.Type == LongDistancePipeType.Fluid
			? "long_distance_fluid_pipeline" : "long_distance_item_pipeline";
		if (ModContent.GetInstance<GregTechCEuTerraria>().TryFind<ModItem>(itemName, out var mi))
			global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(remover, remover.GetSource_Misc("PipeRemove"), mi.Type, 1);
		return true;
	}
}
