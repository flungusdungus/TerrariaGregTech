#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Primitive;

// Port of CokeOvenMachine (+ PrimitiveWorkableMachine collapsed in). Coal/log
// -> coke/charcoal + creosote, no EU. 1 in / 1 out / 1 export-fluid (32B mB).
// Dropped: EnvironmentalHazardEmitterTrait (CO), animateTick particles,
// createUI, onUseWithItem bucket interaction.
public class CokeOvenMachine : WorkableMultiblockMachine,
	ICokeOvenController, IItemHandler, IFluidHandler
{
	protected override string Label => "Coke Oven";

	// Upstream 32 * FluidType.BUCKET_VOLUME.
	private const int CREOSOTE_TANK_CAPACITY = 32 * 1000;

	private NotifiableItemStackHandler? _importItems;
	private NotifiableItemStackHandler? _exportItems;
	private NotifiableFluidTank?        _exportFluids;

	public NotifiableItemStackHandler ImportItems  { get { EnsurePrimitiveTraits(); return _importItems!; } }
	public NotifiableItemStackHandler ExportItems  { get { EnsurePrimitiveTraits(); return _exportItems!; } }
	public NotifiableFluidTank        ExportFluids { get { EnsurePrimitiveTraits(); return _exportFluids!; } }

	public CokeOvenMachine() : base() { }

	protected void EnsurePrimitiveTraits()
	{
		if (_importItems != null) return;
		_importItems  = new NotifiableItemStackHandler(1, IO.IN);
		_exportItems  = new NotifiableItemStackHandler(1, IO.OUT);
		_exportFluids = new NotifiableFluidTank        (1, CREOSOTE_TANK_CAPACITY, IO.OUT);
		Traits.Attach(_importItems);
		Traits.Attach(_exportItems);
		Traits.Attach(_exportFluids);
		Traits.RegisterPersistent("ImportItems",  _importItems);
		Traits.RegisterPersistent("ExportItems",  _exportItems);
		Traits.RegisterPersistent("ExportFluids", _exportFluids);
	}

	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		EnsurePrimitiveTraits();
		base.SaveData(tag);
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		EnsurePrimitiveTraits();
		base.LoadData(tag);
	}

	protected override void OnTick()
	{
		EnsurePrimitiveTraits();
		base.OnTick();
	}

	public override Item[]? GetSlotGroup(SlotGroup group)
	{
		EnsurePrimitiveTraits();
		return group switch
		{
			SlotGroup.InventoryInput  => _importItems!.Storage.Stacks,
			SlotGroup.InventoryOutput => _exportItems!.Storage.Stacks,
			_                         => base.GetSlotGroup(group),
		};
	}

	// IFluidHandler - export-only (creosote). No fluid input; Fill is no-op.

	public override int ResolveFluidTank(IO direction, int localIndex) => localIndex;

	public int        TankCount             { get { EnsurePrimitiveTraits(); return 1; } }
	public FluidStack GetTank(int tank)     { EnsurePrimitiveTraits(); return _exportFluids!.GetFluidInTank(0); }
	public int        GetCapacity(int tank) { EnsurePrimitiveTraits(); return _exportFluids!.GetTankCapacity(0); }
	public bool       IsFluidValid(int tank, FluidStack fluid)
		{ EnsurePrimitiveTraits(); return _exportFluids!.IsFluidValid(0, fluid); }

	public int Fill(FluidStack fluid, bool simulate) => 0;

	public FluidStack Drain(int maxAmount, bool simulate)
		{ EnsurePrimitiveTraits(); return _exportFluids!.Drain(maxAmount, simulate); }

	public FluidStack Drain(FluidStack fluidStack, bool simulate)
		{ EnsurePrimitiveTraits(); return _exportFluids!.Drain(fluidStack, simulate); }

	public IFluidHandler GetTankAccess(int tank) { EnsurePrimitiveTraits(); return _exportFluids!.Storages[0]; }

	// Upstream CokeOvenMachine.java:53 TankWidget(..., true, false).
	public (bool AllowFill, bool AllowDrain) GetTankClickCaps(int tank) => (false, true);

	// IItemHandler - slot 0 = import, slot 1 = export.

	public int  SlotCount                                     { get { EnsurePrimitiveTraits(); return 2; } }
	public Item GetSlot(int slot)                             { EnsurePrimitiveTraits(); return slot == 0 ? _importItems!.GetSlot(0) : _exportItems!.GetSlot(0); }
	public Item Insert(int slot, Item item, bool simulate)    { EnsurePrimitiveTraits(); return slot == 0 ? _importItems!.Insert(0, item, simulate) : item; }
	public Item Extract(int slot, int max, bool simulate)     { EnsurePrimitiveTraits(); return slot == 1 ? _exportItems!.Extract(0, max, simulate) : new Item(); }
	public int  GetSlotLimit(int slot)                        { EnsurePrimitiveTraits(); return slot == 0 ? _importItems!.GetSlotLimit(0) : _exportItems!.GetSlotLimit(0); }
	public bool IsItemValid(int slot, Item item)              { EnsurePrimitiveTraits(); return slot == 0 && _importItems!.IsItemValid(0, item); }
}
