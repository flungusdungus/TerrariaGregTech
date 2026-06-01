#nullable enable
using Terraria;
using Terraria.ModLoader.IO;
using GregTechCEuTerraria.Api;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.Common.Machine.Trait;

// Port of com.gregtechceu.gtceu.common.machine.trait.AutoOutputTrait.
//
// Owns the per-machine auto-output config (output side, enabled flag,
// allow-input-from-output flag) and a TickableSubscription that pushes the
// machine's finished output into adjacent inventories every `ticksPerCycle`
// ticks. Attached by WorkableTieredMachine (all processing machines, mirroring
// upstream SimpleTieredMachine) and SuperTankTileEntity (mirroring upstream
// QuantumTankMachine's `AutoOutputTrait.ofFluids`).
//
// Documented adaptations:
//   - Direction (6 faces) -> IODirection (4 cardinals). Terraria is 2D and our
//     machines are facing-less, so the front-face exclusion is dropped. The
//     player picks a side through the IO config UI panel. The
//     outputDirectionValidator predicates ARE ported - DrumMachine uses the
//     fluid one to lock its output to the DOWN face, verbatim upstream.
//   - itemHandlers/fluidHandlers handler lists -> output slot/tank index
//     ranges on the owning machine; the actual transfer goes through
//     AdjacentItemPush / AdjacentFluidPush (our equivalent of upstream's
//     GTTransferUtils.transferItemsFiltered + the adjacency walk).
//   - IRenderingTrait / IInteractionTrait / IFrontFacingTrait dropped - no
//     soft-mallet / wrench / screwdriver tool plumbing and no 3D block-model
//     state in Terraria.
//   - shouldKeep*Subscription drops upstream's hasAdjacentHandler gate: tML
//     exposes no per-tile neighbor-change event, so a subscription dropped
//     because no inventory was adjacent could never be re-armed. Instead the
//     subscription is purely config-driven (enabled + a side selected), and
//     adjacency is checked inside AdjacentItemPush/FluidPush each cycle. The
//     handler change-listener wiring upstream uses only to re-evaluate that
//     adjacency gate is therefore unnecessary and dropped.
//   - markClientSyncFieldDirty dropped - MachineStateSyncPacket carries the
//     trait's Save() blob to viewers.
public sealed class AutoOutputTrait : MachineTrait
{
	public static readonly MachineTraitType<AutoOutputTrait> TYPE = new(allowMultipleInstances: false);
	public override MachineTraitType TraitType => TYPE;

	// Two attach shapes, mirroring upstream:
	//
	//   1. Handler ref (upstream-shape) - pass an `IItemHandler` / `IFluidHandler`
	//      instance directly. Verbatim with upstream `AutoOutputTrait
	//      .ofItems(IItemHandlerModifiable)` / `.ofFluids(IFluidHandler)`. Push
	//      uses the explicit-handler `AdjacentItemPush.Push(source, handler, ...)`
	//      overload. Right shape for machines with a SEPARATE handler that
	//      isn't part of the machine's own combined IItemHandler face
	//      (Fisher - cache is a standalone trait; QuantumTank - fluid storage
	//      is the machine itself; multi parts that proxy through ItemHandlerProxyTrait).
	//
	//   2. Slot/tank index range on the owning machine's combined IItemHandler /
	//      IFluidHandler face. Right shape for WorkableTieredMachine where
	//      ImportItems + ExportItems are concat'd into one face and auto-output
	//      addresses the export half by index range.
	//
	// Set _itemHandler != null OR _itemSlotCount > 0 - never both.
	private readonly IItemHandler?  _itemHandler;
	private readonly IFluidHandler? _fluidHandler;
	private readonly int _itemSlotStart;
	private readonly int _itemSlotCount;
	private readonly int _fluidTankStart;
	private readonly int _fluidTankCount;

	// State - verbatim upstream field set. Documented divergence from upstream
	// on the output-direction default: upstream defaults to Direction.UP when
	// the machine has no front-facing, so flipping auto-output ON pushes
	// immediately upward. We default to IODirection.None instead, matching the
	// UIDirectionSelector convention (center button = OFF) - the player picks
	// a side explicitly via the IO-config cluster above the machine panel.
	// Direction-locking machines (drum DOWN, etc.) call SetXOutputDirection in
	// their EnsureAutoOutput, which still works against this default.
	private IODirection _itemOutputDirection  = IODirection.None;
	private IODirection _fluidOutputDirection = IODirection.None;
	private bool _autoOutputItems;
	private bool _autoOutputFluids;
	private bool _allowItemInputFromOutputSide;
	private bool _allowFluidInputFromOutputSide;
	private int  _ticksPerCycle = 5;

	// Output-direction validators - verbatim upstream outputDirectionValidator /
	// outputFluidDirectionValidator. A SetXOutputDirection call is rejected when
	// the validator returns false, locking the direction. DrumMachine pins its
	// fluid output to DOWN this way. Default = anything allowed.
	private System.Func<IODirection, bool> _itemOutputDirectionValidator  = _ => true;
	private System.Func<IODirection, bool> _fluidOutputDirectionValidator = _ => true;

	private TickableSubscription? _itemOutputSub;
	private TickableSubscription? _fluidOutputSub;

	public AutoOutputTrait(int itemSlotStart, int itemSlotCount, int fluidTankStart, int fluidTankCount)
	{
		_itemSlotStart  = itemSlotStart;
		_itemSlotCount  = itemSlotCount;
		_fluidTankStart = fluidTankStart;
		_fluidTankCount = fluidTankCount;
	}

	private AutoOutputTrait(IItemHandler? itemHandler, IFluidHandler? fluidHandler)
	{
		_itemHandler  = itemHandler;
		_fluidHandler = fluidHandler;
	}

	// === upstream-shape factories =========================================
	// Mirror upstream's `AutoOutputTrait.ofItems(IItemHandlerModifiable)` /
	// `.ofFluids(IFluidHandler)` - the handler-ref form. AutoOutputItems /
	// Fluids routes through the explicit-handler push overload.
	public static AutoOutputTrait OfItems(IItemHandler  handler) => new(handler, null);
	public static AutoOutputTrait OfFluids(IFluidHandler handler) => new(null,    handler);

	// === slot-range factories =============================================
	// Index range into the owning machine's combined IItemHandler / IFluidHandler
	// face. Used by WTM where ImportItems + ExportItems are concat'd into one
	// face and auto-output addresses the export half by index range.
	public static AutoOutputTrait OfItems(int slotStart, int slotCount)  => new(slotStart, slotCount, 0, 0);
	public static AutoOutputTrait OfFluids(int tankStart, int tankCount) => new(0, 0, tankStart, tankCount);

	// === Support / getters =================================================

	public bool SupportsAutoOutputItems  => _itemHandler  is not null || _itemSlotCount  > 0;
	public bool SupportsAutoOutputFluids => _fluidHandler is not null || _fluidTankCount > 0;

	public IODirection ItemOutputDirection  => SupportsAutoOutputItems  ? _itemOutputDirection  : IODirection.None;
	public IODirection FluidOutputDirection => SupportsAutoOutputFluids ? _fluidOutputDirection : IODirection.None;
	public bool IsAutoOutputItems  => _autoOutputItems;
	public bool IsAutoOutputFluids => _autoOutputFluids;
	public bool AllowItemInputFromOutputSide  => _allowItemInputFromOutputSide;
	public bool AllowFluidInputFromOutputSide => _allowFluidInputFromOutputSide;

	// True when an automated push arriving on `side` of the owning machine must
	// be rejected - `side` is the configured output side and input there is
	// disallowed. Every other side always accepts. Our faceless-2D adaptation of
	// upstream's per-face allowInputFromOutputSide capability gate: the
	// AdjacentItem/FluidPush helpers call this with the side the push lands on.
	// A player's hand interaction never routes through those helpers, so it is
	// never gated.
	public bool BlocksInputFrom(IODirection side, bool fluid)
	{
		if (side == IODirection.None) return false;
		return fluid
			? side == FluidOutputDirection && !_allowFluidInputFromOutputSide
			: side == ItemOutputDirection  && !_allowItemInputFromOutputSide;
	}

	public int TicksPerCycle { get => _ticksPerCycle; set => _ticksPerCycle = value; }

	// === Setters (verbatim upstream - guard on support, re-arm subscription) =

	public void SetAllowAutoOutputItems(bool allow)
	{
		if (!SupportsAutoOutputItems) return;
		_autoOutputItems = allow;
		UpdateItemOutputSubscription();
	}

	public void SetAllowAutoOutputFluids(bool allow)
	{
		if (!SupportsAutoOutputFluids) return;
		_autoOutputFluids = allow;
		UpdateFluidOutputSubscription();
	}

	public void SetItemOutputDirection(IODirection direction)
	{
		if (!SupportsAutoOutputItems) return;
		if (!_itemOutputDirectionValidator(direction)) return;
		_itemOutputDirection = direction;
		UpdateItemOutputSubscription();
	}

	public void SetFluidOutputDirection(IODirection direction)
	{
		if (!SupportsAutoOutputFluids) return;
		if (!_fluidOutputDirectionValidator(direction)) return;
		_fluidOutputDirection = direction;
		UpdateFluidOutputSubscription();
	}

	// Verbatim upstream setOutputDirectionValidator / setFluidOutputDirectionValidator.
	public void SetItemOutputDirectionValidator(System.Func<IODirection, bool> validator) =>
		_itemOutputDirectionValidator = validator;

	public void SetFluidOutputDirectionValidator(System.Func<IODirection, bool> validator) =>
		_fluidOutputDirectionValidator = validator;

	public void SetAllowItemInputFromOutputSide(bool allow)  => _allowItemInputFromOutputSide  = allow;
	public void SetAllowFluidInputFromOutputSide(bool allow) => _allowFluidInputFromOutputSide = allow;

	// === Lifecycle ==========================================================

	public override void OnMachineLoad()
	{
		base.OnMachineLoad();
		UpdateItemOutputSubscription();
		UpdateFluidOutputSubscription();
	}

	public override void OnMachineUnload()
	{
		if (_itemOutputSub != null)  { _itemOutputSub.Unsubscribe();  _itemOutputSub  = null; }
		if (_fluidOutputSub != null) { _fluidOutputSub.Unsubscribe(); _fluidOutputSub = null; }
		base.OnMachineUnload();
	}

	// === Subscription keep-alive ============================================
	// Upstream's shouldKeep*Subscription also checks hasAdjacentHandler; that
	// gate is dropped - see the class-level note. Purely config-driven here.

	private bool ShouldKeepItemSubscription() =>
		SupportsAutoOutputItems && _autoOutputItems && _itemOutputDirection != IODirection.None;

	private bool ShouldKeepFluidSubscription() =>
		SupportsAutoOutputFluids && _autoOutputFluids && _fluidOutputDirection != IODirection.None;

	private void UpdateItemOutputSubscription()
	{
		if (ShouldKeepItemSubscription())
			_itemOutputSub = SubscribeServerTick(_itemOutputSub, AutoOutputItems);
		else if (_itemOutputSub != null)
		{
			_itemOutputSub.Unsubscribe();
			_itemOutputSub = null;
		}
	}

	private void UpdateFluidOutputSubscription()
	{
		if (ShouldKeepFluidSubscription())
			_fluidOutputSub = SubscribeServerTick(_fluidOutputSub, AutoOutputFluids);
		else if (_fluidOutputSub != null)
		{
			_fluidOutputSub.Unsubscribe();
			_fluidOutputSub = null;
		}
	}

	// === Per-tick output (verbatim upstream autoOutputItems / autoOutputFluids) =

	private uint OffsetTimer => (uint)(Main.GameUpdateCount + (uint)(BlockPos.X * 7 + BlockPos.Y * 13));

	private void AutoOutputItems()
	{
		// _ticksPerCycle is in MC ticks (upstream convention). The subscription
		// callback fires every Terraria tick (60 Hz), so gate via FromMcTicks
		// to match upstream's 20 Hz cadence. Default 5 MC ticks = 15 Terraria
		// ticks @ SimSpeed 1.0 (4 Hz).
		if (OffsetTimer % (uint)TickScale.FromMcTicks(_ticksPerCycle) == 0 && _itemOutputDirection != IODirection.None)
		{
			if (_itemHandler is not null)
				// Upstream-shape: push from the bound handler, all of its slots.
				AdjacentItemPush.Push(Machine, _itemHandler, 0, _itemHandler.SlotCount,
					maxPerSlot: 1, side: _itemOutputDirection);
			else
				AdjacentItemPush.Push(Machine, _itemSlotStart, _itemSlotCount,
					maxPerSlot: 1, side: _itemOutputDirection);
		}
		UpdateItemOutputSubscription();
	}

	private void AutoOutputFluids()
	{
		if (OffsetTimer % (uint)TickScale.FromMcTicks(_ticksPerCycle) == 0 && _fluidOutputDirection != IODirection.None)
		{
			if (_fluidHandler is not null)
				AdjacentFluidPush.Push(Machine, _fluidHandler, 0, _fluidHandler.TankCount,
					maxAmount: 1000, side: _fluidOutputDirection);
			else
				AdjacentFluidPush.Push(Machine, _fluidTankStart, _fluidTankCount,
					maxAmount: 1000, side: _fluidOutputDirection);
		}
		UpdateFluidOutputSubscription();
	}

	// === Persistence ========================================================

	public override void Save(TagCompound tag)
	{
		tag["itemDir"]      = (byte)_itemOutputDirection;
		tag["fluidDir"]     = (byte)_fluidOutputDirection;
		tag["autoItem"]     = _autoOutputItems;
		tag["autoFluid"]    = _autoOutputFluids;
		tag["allowItemIn"]  = _allowItemInputFromOutputSide;
		tag["allowFluidIn"] = _allowFluidInputFromOutputSide;
	}

	public override void Load(TagCompound tag)
	{
		if (tag.ContainsKey("itemDir"))      _itemOutputDirection           = (IODirection)tag.GetByte("itemDir");
		if (tag.ContainsKey("fluidDir"))     _fluidOutputDirection          = (IODirection)tag.GetByte("fluidDir");
		if (tag.ContainsKey("autoItem"))     _autoOutputItems               = tag.GetBool("autoItem");
		if (tag.ContainsKey("autoFluid"))    _autoOutputFluids              = tag.GetBool("autoFluid");
		if (tag.ContainsKey("allowItemIn"))  _allowItemInputFromOutputSide  = tag.GetBool("allowItemIn");
		if (tag.ContainsKey("allowFluidIn")) _allowFluidInputFromOutputSide = tag.GetBool("allowFluidIn");
	}

	// Save-compat shim - pull the pre-trait MachineIOConfig `io_*` keys off the
	// machine's top-level save tag (worlds saved before auto-output became a
	// trait). Called once from the owning machine's LoadData.
	public void LoadLegacyIOConfig(TagCompound tag)
	{
		if (tag.ContainsKey("io_isi"))  _itemOutputDirection           = (IODirection)tag.GetByte("io_isi");
		if (tag.ContainsKey("io_fsi"))  _fluidOutputDirection          = (IODirection)tag.GetByte("io_fsi");
		if (tag.ContainsKey("io_iao"))  _autoOutputItems               = tag.GetBool("io_iao");
		if (tag.ContainsKey("io_fao"))  _autoOutputFluids              = tag.GetBool("io_fao");
		if (tag.ContainsKey("io_aifi")) _allowItemInputFromOutputSide  = tag.GetBool("io_aifi");
		if (tag.ContainsKey("io_afif")) _allowFluidInputFromOutputSide = tag.GetBool("io_afif");
		UpdateItemOutputSubscription();
		UpdateFluidOutputSubscription();
	}
}
