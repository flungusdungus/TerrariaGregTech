#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Stable byte ordinals - only add new values at the end. Direction tags
// below mark the INITIATING side; both sides handle every packet.
public enum PacketType : byte
{
	// === Client -> Server (action intents) ===
	MachineViewBegin = 1,    // "I opened this machine's GUI, start syncing me"
	MachineViewEnd   = 2,    // "I closed it, stop"
	SlotAction       = 3,    // Slot interaction (left/right click / shift-click)
	CircuitSet       = 4,    // Circuit selector value
	IOConfigSet      = 5,    // Output side / allow-input-from-output flags
	PowerToggle      = 6,    // Power on/off
	FluidSlotAction  = 7,    // Bucket fill / drain on a fluid slot
	TankConfigSet    = 8,    // Super Tank Locked / Voiding / AutoOutput toggle
	CableSet         = 9,    // Client->Server place a cable / Server->Client broadcast confirm
	CableRemove      = 10,   // Client->Server remove a cable / Server->Client broadcast confirm or burnout
	CableLayerRequest= 11,   // Client->Server "I just joined, send me the cable layer"
	CableLayerFull   = 12,   // Server->Client full cable layer dump
	MachinePlaced    = 13,   // Client->Server "I placed a machine; create the entity authoritatively"
	CoverAction      = 14,   // Client->Server place / remove a cover on a machine side
	TransformerToggle= 15,   // Client->Server flip a transformer's conversion direction (screwdriver)
	ChestAction      = 16,   // Super Chest Locked / Voiding / AutoOutput toggle + Dump button
	// 17, 18 - retired (ChestInsert / TankInteract). Caused a dupe via
	// vanilla SyncEquipment's ignore-self gate. Do NOT reuse - extend at end.
	CoverConfig      = 19,   // Client->Server change one setting of a machine's cover (cover settings UI)
	CoverFilter      = 20,   // Client->Server cover filter edit - matcher slot / blacklist / filter-item slot
	CrateTape        = 21,   // Client->Server right-click a crate with duct/basic tape to seal it
	DrumScrewdriver  = 22,   // Client->Server screwdriver-right-click a drum to toggle auto-output
	PartIoDirection  = 23,   // Client->Server set a multiblock part's IoDirection (the auto-IO push/pull side)
	ParallelSet      = 24,   // Client->Server set a parallel hatch's CurrentParallel
	ActiveRecipeTypeSet = 25, // Client->Server cycle a multi-mode multi's active recipe type
	BoilerThrottleSet   = 26, // Client->Server adjust a Large Boiler's throttle +/-5%
	DistinctSet         = 27, // Client->Server toggle an IDistinctPart bus's distinctness
	JunkToggle          = 28, // Client->Server toggle a fisher's junk-enabled flag
	MachineFilter       = 29, // Client->Server machine filter edit - matcher slot / blacklist / cycle / tag-expression (analogue of CoverFilter for machines that own their own filter)
	CreativeChestSet    = 30, // Client->Server set creative chest source-type / itemsPerCycle / ticksPerCycle
	CreativeTankSet     = 31, // Client->Server set creative tank source-fluid / mBPerCycle / ticksPerCycle
	CreativeEnergySet   = 32, // Client->Server set creative energy container voltage / amps / source / active
	SimplePipeSideSet   = 33, // Client->Server set the 3-state simple-mode (Off / Insert / Extract) on a simple pipe's side

	// === Server -> Client (state sync) ===
	MachineStateSync = 64,   // Full state snapshot of one machine (sent on view begin + periodically)
	SlotUpdate       = 65,   // Single-slot delta (low-bandwidth incremental update)
	FluidUpdate      = 66,   // Single-tank delta
	CursorUpdate     = 67,   // Authoritative cursor (Main.mouseItem) result after a SlotAction
	EnderChannelSync = 68,   // Server->Client virtual ender channel contents (for the ender-cover settings view)
	ActiveCasingSet  = 69,   // Server->Client multiblock active-casing edge (active=true/false, packed cells[])
	ActiveCasingRequest = 70, // Client->Server "I just joined, send me every formed-and-active multi's cells"
	MultiblockFormed = 71,   // Server->Client multiblock formed/unformed edge - toggles IsFormed + IsFlipped on the client controller
	BlockExplosionEffect = 72, // Server->Client play the bomb-sound + smoke-dust at (x, y) - the visual half of a machine self-destruct (boiler water-empty, energy over-voltage). The KillTile half goes through vanilla MessageID.TileManipulation.
	EnergyNetStats   = 73,   // Server->Client per-network throughput broadcast - keyed by each network's anchor cell so wire tooltips on the client can show live extracted / delivered EU.
	PipePlaced       = 74,   // Client->Server place a pipe (item or fluid, kind byte first) / Server->Client broadcast confirm
	PipeRemove       = 75,   // Client->Server remove a pipe (kind byte first) / Server->Client broadcast confirm
	PipeLayerRequest = 76,   // Client->Server "I just joined, send me the pipe layer" (kind byte first)
	PipeLayerFull    = 77,   // Server->Client full pipe layer dump (kind byte first)
	// 78 - retired (PipeSideIoSet); pipe-side state moved to the cover model.
	PipeCoverSync    = 79,   // Server->Client per-cell pipe-cover state broadcast (the analogue of MachineStateSync for pipe-side covers - fires after any cover mutation on a pipe target)
	PipeSideModeSet  = 80,   // Client->Server: set the per-side mode (NotConnected/Passive/Insert/Extract) on a pipe cell. Server resolves to the matching CoverBehavior and broadcasts via PipeCoverSync.
	PipeStats        = 81,   // Server->Client periodic broadcast of per-pipe transferred-item counts so the pipe panel's "Last 20t" line shows live counts on MP clients (analogue of EnergyNetStatsPacket).
	FluidPipeStats   = 82,   // Server->Client periodic broadcast of per-fluid-pipe tank contents so the pipe panel's "Last 1s" line shows real fluid amounts on MP clients (fluid analog of PipeStats).
	ProfilerSync     = 83,   // Server->Client periodic broadcast of the server's profiler counter snapshot. Counters land on the client under category="server.<original-category>" so the UI shows server + client metrics side-by-side.
	ItemCollectEffect = 84,  // Server->Client play the item-collected sparkle dust at one or more (x, y) tile coords - the visual half of an Item Collector consuming dropped items in-place (Dust.NewDust no-ops on a dedicated server, and the collection runs server-only). Batched: one packet per machine per tick.
	LdEndpointToggle = 85,   // Client->Server flip a long-distance pipeline endpoint's IO role (screwdriver IN<->OUT). Same shape as TransformerToggle - re-sync via TileEntitySharing.
	MachineEnergySync = 86,  // Server->Client compact per-machine energy-stored sync (Point16 + long, ~12 B). Energy is OMITTED from the full MachineStateSync blob (NEC.SaveForSync / WEMM.SaveDataForSync) so per-tick energy jitter stops re-sending the whole blob; this carries it cheaply instead. Mirror of upstream's per-field @SyncToClient energyStored.
}
