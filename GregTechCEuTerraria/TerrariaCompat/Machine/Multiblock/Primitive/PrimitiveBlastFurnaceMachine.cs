#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Primitive;

// Port of PrimitiveBlastFurnaceMachine (+ PrimitiveWorkableMachine collapsed
// in). Iron + fuel + igniter -> steel + dust x 2, no EU, fluidless.
// 3 in / 3 out handlers with HandlerIO (IO.IN, IO.NONE) / (IO.OUT, IO.NONE) -
// upstream's blocked-adjacent-extraction override.
// Dropped: EnvironmentalHazardEmitterTrait (CO), MultiblockFluidRendererTrait,
// animateTick particles, hurtEntitiesAndBreakSnow.
public class PrimitiveBlastFurnaceMachine : WorkableMultiblockMachine,
	IItemHandler
{
	protected override string Label => "Primitive Blast Furnace";

	private const int InputSlots  = 3;
	private const int OutputSlots = 3;

	private NotifiableItemStackHandler? _importItems;
	private NotifiableItemStackHandler? _exportItems;

	public NotifiableItemStackHandler ImportItems { get { EnsurePrimitiveTraits(); return _importItems!; } }
	public NotifiableItemStackHandler ExportItems { get { EnsurePrimitiveTraits(); return _exportItems!; } }

	public PrimitiveBlastFurnaceMachine() : base() { }

	public override Api.Recipe.Modifier.RecipeModifier GetRecipeModifier() =>
		Definition?.MultiRecipeModifier ?? Api.Recipe.Modifier.RecipeModifier.NO_MODIFIER;

	protected void EnsurePrimitiveTraits()
	{
		if (_importItems != null) return;
		_importItems = new NotifiableItemStackHandler(InputSlots,  IO.IN,  IO.NONE);
		_exportItems = new NotifiableItemStackHandler(OutputSlots, IO.OUT, IO.NONE);
		Traits.Attach(_importItems);
		Traits.Attach(_exportItems);
		Traits.RegisterPersistent("ImportItems", _importItems);
		Traits.RegisterPersistent("ExportItems", _exportItems);
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

	// IItemHandler - flat view: input [0..2], output [3..5].

	public int  SlotCount                                  { get { EnsurePrimitiveTraits(); return InputSlots + OutputSlots; } }
	public Item GetSlot(int slot)                          { EnsurePrimitiveTraits(); return slot < InputSlots ? _importItems!.GetSlot(slot) : _exportItems!.GetSlot(slot - InputSlots); }
	public Item Insert(int slot, Item item, bool simulate) { EnsurePrimitiveTraits(); return slot < InputSlots ? _importItems!.Insert(slot, item, simulate) : item; }
	public Item Extract(int slot, int max, bool simulate)  { EnsurePrimitiveTraits(); return slot >= InputSlots ? _exportItems!.Extract(slot - InputSlots, max, simulate) : new Item(); }
	public int  GetSlotLimit(int slot)                     { EnsurePrimitiveTraits(); return slot < InputSlots ? _importItems!.GetSlotLimit(slot) : _exportItems!.GetSlotLimit(slot - InputSlots); }
	public bool IsItemValid(int slot, Item item)           { EnsurePrimitiveTraits(); return slot < InputSlots && _importItems!.IsItemValid(slot, item); }
}
