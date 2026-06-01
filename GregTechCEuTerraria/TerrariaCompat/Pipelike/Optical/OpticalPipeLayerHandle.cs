#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// Optical-pipe place/cut/refund cycle. Singleton, parallel to
// LaserPipeLayerHandle. Empty cell payload - just gate + set + broadcast +
// refund.
public sealed class OpticalPipeLayerHandle : IGridLayerHandle
{
	public static readonly OpticalPipeLayerHandle Instance = new();
	private OpticalPipeLayerHandle() { }

	public bool Has(int x, int y) => OpticalPipeLayerSystem.Pipes.Has(x, y);

	public bool TryPlace(int x, int y, Player placer)
	{
		if (OpticalPipeLayerSystem.Pipes.Has(x, y)) return false;
		// Compute the <=2 reciprocal pipe connections (non-splitting) and store
		// the open-mask on the cell + neighbours (also marks the layer dirty so
		// the net rebuilds from the masks).
		OpticalConn.ConnectOnPlace(OpticalPipeLayerSystem.Pipes, x, y);
		OpticalPipeNetSystem.OnPipeAdded(x, y);
		PipePackets.SendPlacedOptical(x, y);
		return true;
	}

	public bool CutAt(int x, int y, Player remover)
	{
		if (!OpticalPipeLayerSystem.Pipes.Has(x, y)) return false;
		OpticalPipeLayerSystem.Pipes.Remove(x, y);
		OpticalConn.ClearOnRemove(OpticalPipeLayerSystem.Pipes, x, y);
		OpticalPipeNetSystem.OnPipeRemoved(x, y);
		PipePackets.SendRemove(x, y, PipeKind.Optical);
		if (ModContent.GetInstance<GregTechCEuTerraria>().TryFind<ModItem>("normal_optical_pipe", out var mi))
			global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(remover, remover.GetSource_Misc("PipeRemove"), mi.Type, 1);
		return true;
	}
}
