#nullable enable
using System.Diagnostics;
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Central packet dispatcher. Adding a new packet = bump PacketType enum +
// add a case here + ship its Send/Handle methods on a packet class.
public static class NetRouter
{
	private static Mod? _mod;
	public static Mod Mod => _mod ??= ModLoader.GetMod("GregTechCEuTerraria");

	public static void Handle(BinaryReader reader, int whoAmI)
	{
		// reader.BaseStream.Length is the 131070-byte tML receive buffer, NOT
		// the message size. Real size = Position delta across the handler.
		long entryPos = reader.BaseStream.Position;
		var type = (PacketType)reader.ReadByte();
		string typeName = type.ToString();
		Profiler.Profiler.Count("net.in.count", typeName);
		// Time the dispatch so client-side packet-handler cost folds into
		// aggregate.frame_budget_ms_s. Before this, only ProfilerSync's
		// handler was timed; the 2026-05-31 FPS=1 spike (8940 MachineStateSync
		// packets in 1s) only registered 211 ms of timed work even though
		// the frame took ~1000 ms - the missing ~800 ms was here.
		long handleT0 = Stopwatch.GetTimestamp();
		switch (type)
		{
			case PacketType.MachineViewBegin: MachineViewPacket.HandleBegin(reader, whoAmI); break;
			case PacketType.MachineViewEnd:   MachineViewPacket.HandleEnd  (reader, whoAmI); break;
			case PacketType.MachineStateSync: MachineStateSyncPacket.HandleOnClient(reader); break;
			case PacketType.MachineEnergySync: MachineEnergySyncPacket.HandleOnClient(reader); break;
			case PacketType.PowerToggle:      MachineActions.HandleIncoming<PowerToggleAction>(reader, whoAmI); break;
			case PacketType.PartIoDirection:  MachineActions.HandleIncoming<PartIoDirectionSetAction>(reader, whoAmI); break;
			case PacketType.ParallelSet:      MachineActions.HandleIncoming<ParallelSetAction>(reader, whoAmI); break;
			case PacketType.ActiveRecipeTypeSet: MachineActions.HandleIncoming<ActiveRecipeTypeSetAction>(reader, whoAmI); break;
			case PacketType.BoilerThrottleSet: MachineActions.HandleIncoming<BoilerThrottleSetAction>(reader, whoAmI); break;
			case PacketType.DistinctSet:      MachineActions.HandleIncoming<DistinctSetAction>(reader, whoAmI); break;
			case PacketType.JunkToggle:       MachineActions.HandleIncoming<JunkToggleAction>(reader, whoAmI); break;
			case PacketType.CircuitSet:       MachineActions.HandleIncoming<CircuitSetAction>(reader, whoAmI); break;
			case PacketType.IOConfigSet:      MachineActions.HandleIncoming<IOConfigSetAction>(reader, whoAmI); break;
			case PacketType.TankConfigSet:    MachineActions.HandleIncoming<TankConfigSetAction>(reader, whoAmI); break;
			case PacketType.ChestAction:      MachineActions.HandleIncoming<ChestAction>(reader, whoAmI); break;
			case PacketType.SlotAction:       MachineActions.HandleIncoming<SlotAction>(reader, whoAmI); break;
			case PacketType.FluidSlotAction:  MachineActions.HandleIncoming<FluidSlotAction>(reader, whoAmI); break;
			case PacketType.CableSet:         CablePackets.HandleSet(reader, whoAmI); break;
			case PacketType.CableRemove:      CablePackets.HandleRemove(reader, whoAmI); break;
			case PacketType.CableLayerRequest:CablePackets.HandleLayerRequest(reader, whoAmI); break;
			case PacketType.CableLayerFull:   CablePackets.HandleLayerFull(reader); break;
			case PacketType.MachinePlaced:    MachinePlacedPacket.Handle(reader, whoAmI); break;
			case PacketType.CoverAction:      CoverActions.HandleIncoming<CoverAction>(reader, whoAmI); break;
			case PacketType.CoverConfig:      CoverActions.HandleIncoming<CoverConfigAction>(reader, whoAmI); break;
			case PacketType.CoverFilter:      CoverActions.HandleIncoming<CoverFilterAction>(reader, whoAmI); break;
			case PacketType.MachineFilter:    MachineActions.HandleIncoming<MachineFilterAction>(reader, whoAmI); break;
			case PacketType.CreativeChestSet: MachineActions.HandleIncoming<CreativeChestSetAction>(reader, whoAmI); break;
			case PacketType.CreativeTankSet:  MachineActions.HandleIncoming<CreativeTankSetAction>(reader, whoAmI); break;
			case PacketType.CreativeEnergySet:MachineActions.HandleIncoming<CreativeEnergySetAction>(reader, whoAmI); break;
			case PacketType.TransformerToggle:TransformerTogglePacket.Handle(reader, whoAmI); break;
			case PacketType.LdEndpointToggle: LdEndpointTogglePacket.Handle(reader, whoAmI); break;
			// 17 (ChestInsert) / 18 (TankInteract) retired - see PacketType.cs.
			case PacketType.CrateTape:        CrateTapePacket.Handle(reader, whoAmI); break;
			case PacketType.DrumScrewdriver:  DrumScrewdriverPacket.Handle(reader, whoAmI); break;
			case PacketType.CursorUpdate:     CursorUpdatePacket.HandleOnClient(reader); break;
			case PacketType.EnderChannelSync: EnderChannelSyncPacket.HandleOnClient(reader); break;
			case PacketType.ActiveCasingSet:     ActiveCasingPacket.HandleSet(reader); break;
			case PacketType.ActiveCasingRequest: ActiveCasingPacket.HandleRequest(reader, whoAmI); break;
			case PacketType.MultiblockFormed:    MultiblockFormedPacket.HandleSet(reader); break;
			case PacketType.BlockExplosionEffect: BlockExplosionEffectPacket.HandleOnClient(reader); break;
			case PacketType.EnergyNetStats:   EnergyNetStatsPacket.HandleOnClient(reader); break;
			case PacketType.PipePlaced:       PipePackets.HandlePlaced(reader, whoAmI); break;
			case PacketType.PipeRemove:       PipePackets.HandleRemove(reader, whoAmI); break;
			case PacketType.PipeLayerRequest: PipePackets.HandleLayerRequest(reader, whoAmI); break;
			case PacketType.PipeLayerFull:    PipePackets.HandleLayerFull(reader); break;
			case PacketType.PipeCoverSync:    PipeCoverSyncPacket.HandleOnClient(reader); break;
			case PacketType.PipeSideModeSet:  PipeSideModePacket.HandleSet(reader, whoAmI); break;
			case PacketType.SimplePipeSideSet: SimplePipeSideSetPacket.HandleSet(reader, whoAmI); break;
			case PacketType.PipeStats:        PipeStatsPacket.HandleOnClient(reader); break;
			case PacketType.FluidPipeStats:   FluidPipeStatsPacket.HandleOnClient(reader); break;
			case PacketType.ProfilerSync:     ProfilerSyncPacket.HandleOnClient(reader); break;
			case PacketType.ItemCollectEffect: ItemCollectEffectPacket.HandleOnClient(reader); break;
			default:
				NetHelpers.LogBadPacket("dispatch", $"unknown PacketType={(byte)type} from whoAmI={whoAmI}");
				break;
		}
		Profiler.Profiler.AccumulateTimer("net.handle", typeName, Stopwatch.GetTimestamp() - handleT0);
		Profiler.Profiler.Count("net.in.bytes", typeName, reader.BaseStream.Position - entryPos);
	}

	// Allocate a packet pre-stamped with its type byte. Routes the type
	// ordinal through one file.
	public static ModPacket NewPacket(PacketType type)
	{
		// Count-only: ModPacket is sealed, no after-Send hook for byte size.
		// `net.in.bytes.<type>` on the receiving side covers size.
		Profiler.Profiler.Count("net.out.count", type.ToString());
		var p = Mod.GetPacket();
		p.Write((byte)type);
		return p;
	}
}
