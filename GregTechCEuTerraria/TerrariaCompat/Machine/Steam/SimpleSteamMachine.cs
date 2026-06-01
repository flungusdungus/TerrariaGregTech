#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Common.Energy;
using Terraria;
using RecipeIO = GregTechCEuTerraria.Api.Capability.Recipe.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

// Port of SimpleSteamMachine. Runs electric recipes but pays EU as steam via
// SteamEnergyRecipeHandler (1 mB/EU LP, 2 mB/EU HP). LV-tier-capped; LP runs
// 2x duration. ExhaustVentMachineTrait + VentCondition omitted (no 2D vent).
public class SimpleSteamMachine : SteamWorkableMachine, IItemHandler
{
	public SimpleSteamMachine() : base() { }

	protected override string Label => Definition?.Label ?? "Steam Machine";
	public override GTRecipeType GetRecipeType() => Definition?.RecipeType!;

	// mB/EU - upstream 1.0 LP / 2.0 HP.
	public double ConversionRate => IsHighPressure ? 2.0 : 1.0;

	// Steam tank is INPUT here (boiler/pipe pushes in); base defaults to OUT.
	protected override NotifiableFluidTank CreateSteamTank() =>
		new(1, SteamTankCapacity, RecipeIO.IN);

	// SteamMachine.Fill rejects all fill (boiler tank is OUT). Processing
	// machine needs INPUT fill from adjacent boilers / steam pipes.
	public override bool IsFluidValid(int tank, FluidStack fluid) =>
		!fluid.IsEmpty && fluid.Type!.Id == FluidRegistry.Steam.Id;

	public override int Fill(FluidStack fluid, bool simulate)
	{
		if (fluid.IsEmpty || fluid.Type!.Id != FluidRegistry.Steam.Id) return 0;
		EnsureSteamTraits();
		return SteamTank.FillInternal(fluid, simulate);
	}

	private NotifiableItemStackHandler? _importItems;
	private NotifiableItemStackHandler? _exportItems;
	private SteamEnergyRecipeHandler?   _steamEnergy;

	public NotifiableItemStackHandler ImportItems { get { EnsureSteamTraits(); return _importItems!; } }
	public NotifiableItemStackHandler ExportItems { get { EnsureSteamTraits(); return _exportItems!; } }

	protected override void EnsureSteamTraits()
	{
		base.EnsureSteamTraits();
		if (_importItems is not null) return;
		BindDefinition();

		int inSlots  = Definition?.InputSlotCount  ?? 1;
		int outSlots = Definition?.OutputSlotCount ?? 1;
		_importItems = new NotifiableItemStackHandler(inSlots,  RecipeIO.IN);
		_exportItems = new NotifiableItemStackHandler(outSlots, RecipeIO.OUT);
		Traits.Attach(_importItems);
		Traits.Attach(_exportItems);
		Traits.RegisterPersistent("ImportItems", _importItems);
		Traits.RegisterPersistent("ExportItems", _exportItems);

		// Mirrors upstream onLoad: RecipeHandlerList.of(IO.IN,
		// new SteamEnergyRecipeHandler(steamTank, rate)).
		_steamEnergy = new SteamEnergyRecipeHandler(SteamTank, ConversionRate);
	}

	// SteamWorkableMachine stubs these (boilers); processing machines route EU
	// through the steam-energy handler.
	protected override long EnergyStoredCore
	{
		get { EnsureSteamTraits(); return _steamEnergy!.StoredEu; }
	}

	protected override ActionResult TryDrainEUCore(long voltage)
	{
		EnsureSteamTraits();
		return _steamEnergy!.TryDrainEnergy(voltage, simulate: false)
			? ActionResult.SUCCESS
			: ActionResult.Fail("gtceu.recipe.insufficient_eu", Api.Capability.Recipe.EURecipeCapability.CAP, RecipeIO.IN);
	}

	// Verbatim: reject above LV; x2 duration on LP. Vent reject + VentCondition
	// dropped (see header).
	public override GTRecipe? FullModifyRecipe(GTRecipe recipe)
	{
		if (RecipeHelper.GetRecipeEUtTier(recipe) > (int)VoltageTier.LV)
			return null;
		if (!IsHighPressure)
			return recipe.Copy(ContentModifier.Multiplier_(2.0), modifyDuration: true);
		return recipe;
	}

	// Combined surface: slots [0..InCount) = import; rest = export.

	public int SlotCount { get { EnsureSteamTraits(); return _importItems!.SlotCount + _exportItems!.SlotCount; } }

	public Item GetSlot(int s)
	{
		EnsureSteamTraits();
		int n = _importItems!.SlotCount;
		return s < n ? _importItems.GetSlot(s) : _exportItems!.GetSlot(s - n);
	}

	public Item Insert(int s, Item item, bool simulate)
	{
		EnsureSteamTraits();
		int n = _importItems!.SlotCount;
		return s < n ? _importItems.Insert(s, item, simulate)
		             : _exportItems!.Insert(s - n, item, simulate);
	}

	public Item Extract(int s, int maxAmount, bool simulate)
	{
		EnsureSteamTraits();
		int n = _importItems!.SlotCount;
		return s < n ? _importItems.Extract(s, maxAmount, simulate)
		             : _exportItems!.Extract(s - n, maxAmount, simulate);
	}

	public bool IsItemValid(int slot, Item item)
	{
		EnsureSteamTraits();
		// Import accepts anything (recipe match is the real gate); export = output-only.
		return slot < _importItems!.SlotCount;
	}

	public Item[] Slots
	{
		get
		{
			EnsureSteamTraits();
			var inp  = _importItems!.Storage.Stacks;
			var outp = _exportItems!.Storage.Stacks;
			var combined = new Item[inp.Length + outp.Length];
			System.Array.Copy(inp,  0, combined, 0,          inp.Length);
			System.Array.Copy(outp, 0, combined, inp.Length, outp.Length);
			return combined;
		}
	}

	public override Item[]? GetSlotGroup(SlotGroup group) => group switch
	{
		SlotGroup.Inventory       => Slots,
		SlotGroup.InventoryInput  => ImportItems.Storage.Stacks,
		SlotGroup.InventoryOutput => ExportItems.Storage.Stacks,
		_                         => base.GetSlotGroup(group),
	};

	public override void NotifySlotGroupChanged(SlotGroup group)
	{
		if (group == SlotGroup.InventoryInput)       ImportItems.OnContentsChanged();
		else if (group == SlotGroup.InventoryOutput) ExportItems.OnContentsChanged();
	}

	// UI accessors (parity with WorkableTieredMachine).
	public int  InputSlots  => Definition?.InputSlotCount  ?? 0;
	public int  OutputSlots => Definition?.OutputSlotCount ?? 0;
	public bool UsesCircuit => Definition?.UsesCircuit ?? false;
}
