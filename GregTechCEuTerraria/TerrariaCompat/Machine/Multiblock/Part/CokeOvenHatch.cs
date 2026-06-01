#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of CokeOvenHatch. Single I/O face for the Coke Oven multi - 3 proxy
// traits (input items / output items / output creosote) bound to the controller
// on AddedToController. Auto-output every 5 ticks on the configured IoDirection.
//
// Item input+output collapsed into one IoFacadeHandler (operation-type
// dispatch); upstream uses per-side Forge caps. ICokeOvenController decouples
// us from the unported CokeOvenMachine class.
public class CokeOvenHatch : MultiblockPartMachine
{
	protected override string Label => "Coke Oven Hatch";

	public ItemHandlerProxyTrait? InputInventory  { get; protected set; }
	public ItemHandlerProxyTrait? OutputInventory { get; protected set; }
	public FluidTankProxyTrait?   Tank            { get; protected set; }

	public IODirection IoDirection { get; protected set; } = IODirection.Up;
	public void SetIoDirection(IODirection direction)
	{
		if (IoDirection == direction) return;
		IoDirection = direction;
		if (IsServer) MachineStateSyncPacket.Broadcast(this);
	}

	private IoFacadeHandler? _itemFacade;
	public override IItemHandler? ExposedItemHandler  => _itemFacade;
	public override IFluidHandler? ExposedFluidHandler => Tank;

	private TickableSubscription? _autoIOSubs;

	public CokeOvenHatch() : base() { }

	public void Configure()
	{
		EnsureTraits();
		EnsureAutoIOSubscription();
	}

	// Traits must exist before the controller's OnStructureFormed binds them;
	// per-part Configure() doesn't fire so first-tick init is the wire-up point.
	protected override void OnTick()
	{
		EnsureTraits();
		EnsureAutoIOSubscription();
		base.OnTick();
	}

	private void EnsureTraits()
	{
		if (InputInventory != null) return;
		InputInventory  = new ItemHandlerProxyTrait(IO.IN);
		OutputInventory = new ItemHandlerProxyTrait(IO.OUT);
		Tank            = new FluidTankProxyTrait(IO.BOTH);
		Traits.Attach(InputInventory);
		Traits.Attach(OutputInventory);
		Traits.Attach(Tank);
		_itemFacade = new IoFacadeHandler(InputInventory, OutputInventory);
	}

	private void EnsureAutoIOSubscription()
	{
		_autoIOSubs ??= SubscribeServerTick(AutoIOTick);
	}

	public bool CanShared() => false;

	public override void AddedToController(MultiblockControllerMachine controller)
	{
		base.AddedToController(controller);
		// TileEntity.ByID iteration order isn't guaranteed - controller may
		// form + bind us before our OnTick has fired.
		EnsureTraits();
		if (controller is ICokeOvenController cokeOven)
		{
			InputInventory!.Proxy  = cokeOven.ImportItems;
			OutputInventory!.Proxy = cokeOven.ExportItems;
			Tank!.Proxy            = cokeOven.ExportFluids;
		}
	}

	public override void RemovedFromController(MultiblockControllerMachine controller)
	{
		base.RemovedFromController(controller);
		if (InputInventory  != null) InputInventory.Proxy  = null;
		if (OutputInventory != null) OutputInventory.Proxy = null;
		if (Tank            != null) Tank.Proxy            = null;
	}

	private void AutoIOTick()
	{
		if (GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(5) != 0) return;
		if (!WorkingEnabled) return;
		if (OutputInventory != null && !OutputInventory.IsEmpty())
			OutputInventory.ExportToNearby(IoDirection);
		if (Tank != null && !Tank.IsEmpty())
			Tank.ExportToNearby(IoDirection);
	}

	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["ioDirection"] = (byte)IoDirection;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("ioDirection")) IoDirection = (IODirection)tag.GetByte("ioDirection");
		EnsureTraits();
		EnsureAutoIOSubscription();
	}

	// Virtual slot space: [0, input.SlotCount) = input, rest = output.
	// Inserts route to input only; extracts to output only.
	private sealed class IoFacadeHandler : IItemHandler
	{
		private readonly ItemHandlerProxyTrait _input;
		private readonly ItemHandlerProxyTrait _output;

		public IoFacadeHandler(ItemHandlerProxyTrait input, ItemHandlerProxyTrait output)
		{
			_input  = input;
			_output = output;
		}

		public int SlotCount => _input.SlotCount + _output.SlotCount;

		public Item GetSlot(int slot)
		{
			if (slot < _input.SlotCount) return _input.GetSlot(slot);
			return _output.GetSlot(slot - _input.SlotCount);
		}

		public Item Insert(int slot, Item item, bool simulate)
		{
			if (slot < _input.SlotCount) return _input.Insert(slot, item, simulate);
			return item;
		}

		public Item Extract(int slot, int maxAmount, bool simulate)
		{
			if (slot < _input.SlotCount) return new Item();
			return _output.Extract(slot - _input.SlotCount, maxAmount, simulate);
		}

		public int GetSlotLimit(int slot) =>
			slot < _input.SlotCount ? _input.GetSlotLimit(slot)
									: _output.GetSlotLimit(slot - _input.SlotCount);

		public bool IsItemValid(int slot, Item item) =>
			slot < _input.SlotCount && _input.IsItemValid(slot, item);
	}
}
