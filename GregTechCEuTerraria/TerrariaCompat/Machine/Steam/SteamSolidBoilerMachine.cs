#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

// Port of SteamSolidBoilerMachine. Adds fuel-in + ash-out slots on top of
// SteamBoilerMachine; ash rolls at afterWorking via getBurningFuelRemainder.
// FluidUtil.getFluidContained bucket-filter dropped (vanilla buckets differ).
// ChemicalHelper.getMaterialStack -> MaterialItemRegistry lookup; missing
// ash/dark_ash items return 0 silently.
public class SteamSolidBoilerMachine : SteamBoilerMachine, IItemHandler
{
	public SteamSolidBoilerMachine() : base() { }
	protected SteamSolidBoilerMachine(bool isHighPressure) : base(isHighPressure) { }

	protected override string Label => Definition?.Label ?? "Boiler";

	public override GTRecipeType GetRecipeType() => Definition?.RecipeType!;

	// STEAM_BOILER is shared - hide fluid-fuel recipes (liquid boiler's).
	public override bool ShowsInRecipeBrowser(GTRecipe recipe) =>
		recipe.GetInputContents(ItemRecipeCapability.CAP).Count > 0;

	private NotifiableItemStackHandler? _fuelHandler;
	private NotifiableItemStackHandler? _ashHandler;
	public NotifiableItemStackHandler FuelHandler { get { EnsureSteamTraits(); return _fuelHandler!; } }
	public NotifiableItemStackHandler AshHandler  { get { EnsureSteamTraits(); return _ashHandler!; } }

	protected override void EnsureSteamTraits()
	{
		base.EnsureSteamTraits();
		if (_fuelHandler is not null) return;

		// IN/IN - external pipes insert but can't extract.
		_fuelHandler = new NotifiableItemStackHandler(1, Api.Capability.Recipe.IO.IN, Api.Capability.Recipe.IO.IN);
		_fuelHandler.SetFilter(IsFuelItem);
		Traits.Attach(_fuelHandler);
		Traits.RegisterPersistent("FuelHandler", _fuelHandler);

		_ashHandler = new NotifiableItemStackHandler(1, Api.Capability.Recipe.IO.OUT, Api.Capability.Recipe.IO.OUT);
		Traits.Attach(_ashHandler);
		Traits.RegisterPersistent("AshHandler", _ashHandler);
	}

	// Verbatim ConfigHolder defaults: 120 LP / 300 HP mB/sec at max temp.
	protected override long GetBaseSteamOutput() => IsHighPressure ? 300 : 120;

	private static readonly Dictionary<int, bool> _fuelCache = new();

	private bool IsFuelItem(Item item)
	{
		if (item is null || item.IsAir) return false;
		if (_fuelCache.TryGetValue(item.type, out bool cached)) return cached;
		bool ok = false;
		foreach (var recipe in RecipeRegistry.ForStation(GetRecipeType().RegistryName))
		{
			foreach (var c in recipe.GetInputContents(ItemRecipeCapability.CAP))
			{
				var ing = (Ingredient)c.Payload;
				if (ing.Test(item)) { ok = true; break; }
			}
			if (ok) break;
		}
		_fuelCache[item.type] = ok;
		return ok;
	}

	public override void AfterWorking()
	{
		base.AfterWorking();
		var lastRecipe = Recipe.GetLastRecipe();
		if (lastRecipe is null) return;

		var inputs = lastRecipe.GetInputContents(ItemRecipeCapability.CAP);
		if (inputs.Count == 0) return;

		var ing = PeelToInner((Ingredient)inputs[0].Payload);
		int fuelType = PrimaryItemType(ing);
		if (fuelType <= 0) return;

		int remainder = GetBurningFuelRemainder(fuelType);
		if (remainder <= 0) return;

		var ash = new Item();
		ash.SetDefaults(remainder);
		ash.stack = 1;
		_ashHandler!.Insert(0, ash, simulate: false);
	}

	// Upstream chemicals: Charcoal->Ash 30%, Coal->DarkAsh 35%, Coke->Ash 50%.
	private static readonly Random _ashRng = new();
	private static int GetBurningFuelRemainder(int fuelItemType)
	{
		string? fuelMat = null;
		foreach (var (matId, prefixId, item) in MaterialItemRegistry.All)
		{
			if (item.Type != fuelItemType) continue;
			fuelMat = matId;
			break;
		}
		if (fuelMat is null) return 0;

		string? ashMat;
		float chance;
		switch (fuelMat)
		{
			case "charcoal": ashMat = "ash";      chance = 0.30f; break;
			case "coal":     ashMat = "dark_ash"; chance = 0.35f; break;
			case "coke":     ashMat = "ash";      chance = 0.50f; break;
			default:         return 0;
		}
		if (_ashRng.NextDouble() > chance) return 0;
		return MaterialItemRegistry.Get(ashMat, "dust") ?? 0;
	}

	private static Ingredient PeelToInner(Ingredient ing) => ing switch
	{
		SizedIngredient sized          => PeelToInner(sized.Inner),
		IntProviderIngredient ipi      => PeelToInner(ipi.Inner),
		_                              => ing,
	};

	private static int PrimaryItemType(Ingredient ing) => ing switch
	{
		ItemStackIngredient isi    => isi.ItemType,
		NBTPredicateIngredient nbt => nbt.ItemType,
		TagIngredient tag          => tag.GetItems().Count > 0 ? tag.GetItems()[0].type : 0,
		_                          => 0,
	};

	// Combined surface: slot 0 = fuel, slot 1 = ash.

	public int SlotCount       { get { EnsureSteamTraits(); return _fuelHandler!.SlotCount + _ashHandler!.SlotCount; } }
	public Item GetSlot(int s) { EnsureSteamTraits(); return s == 0 ? _fuelHandler!.GetSlot(0) : _ashHandler!.GetSlot(0); }
	public Item Insert(int s, Item item, bool simulate)
	{
		EnsureSteamTraits();
		return s == 0 ? _fuelHandler!.Insert(0, item, simulate) : _ashHandler!.Insert(0, item, simulate);
	}
	public Item Extract(int s, int maxAmount, bool simulate)
	{
		EnsureSteamTraits();
		return s == 0 ? _fuelHandler!.Extract(0, maxAmount, simulate) : _ashHandler!.Extract(0, maxAmount, simulate);
	}
	public bool IsItemValid(int slot, Item item)
	{
		if (slot == 0) return IsFuelItem(item);
		// Ash IO.OUT - no external insert.
		return false;
	}

	public Item[] Slots
	{
		get
		{
			EnsureSteamTraits();
			return new[] { _fuelHandler!.GetSlot(0), _ashHandler!.GetSlot(0) };
		}
	}

	public override Item[]? GetSlotGroup(SlotGroup group) => group switch
	{
		// Concat snapshot - for OnKill drop walker / label readers.
		SlotGroup.Inventory       => Slots,
		// SlotAction.Apply does a ref-write; must be the backing Storage.Stacks.
		SlotGroup.InventoryInput  => FuelHandler.Storage.Stacks,
		SlotGroup.InventoryOutput => AshHandler.Storage.Stacks,
		_                         => base.GetSlotGroup(group),
	};

	public override void NotifySlotGroupChanged(SlotGroup group)
	{
		if (group == SlotGroup.InventoryInput)       FuelHandler.OnContentsChanged();
		else if (group == SlotGroup.InventoryOutput) AshHandler.OnContentsChanged();
	}

	// No ash auto-output (upstream parity). Steam auto-output inherited from base.
}
