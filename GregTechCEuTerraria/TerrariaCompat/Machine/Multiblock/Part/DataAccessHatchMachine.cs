#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Recipe.Condition;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of DataAccessHatchMachine. Holds data sticks for the controller (Assembly
// Line / Data Bank); recipes gated by ResearchCondition only run if their stick
// is loaded. Tier -> slots: HV=4, EV=9, LuV=16, else=1. Creative=0 slots, unlocks all.
//
// ResearchManager STUBBED until the research subsystem lands - RebuildData
// always clears the set so this hatch contributes no unlocks today. Non-research
// recipes pass through normally.
public class DataAccessHatchMachine : TieredPartMachine, IDataAccessHatch
{
	protected override string Label => "Data Access Hatch";

	public bool IsCreativeHatch { get; protected set; }

	public NotifiableItemStackHandler? ImportItems { get; protected set; }

	// Build-then-swap (volatile + reference assignment): defensive only - the
	// actual server hang it was added for was the C# DIM-shadowing trap in
	// ModifyRecipe (see that method). Don't chase HashSet theories.
	private volatile HashSet<GTRecipe> _recipes = new();

	// _recipes is server-only (upstream rebuildData early-returns on client + not
	// @SaveField). This synced count drives the client-readable hover diagnostic.
	protected int _syncResearchCount;

	public override Api.Capability.IItemHandler? ExposedItemHandler => ImportItems;

	// Expose the data-item inventory to the slot-widget / SlotAction path.
	public override Item[]? GetSlotGroup(TerrariaCompat.Machine.SlotGroup group)
	{
		if (ImportItems == null) return base.GetSlotGroup(group);
		return group is TerrariaCompat.Machine.SlotGroup.InventoryInput
			or TerrariaCompat.Machine.SlotGroup.Inventory
			? ImportItems.Storage.Stacks : base.GetSlotGroup(group);
	}

	public DataAccessHatchMachine() : base() { }

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		Configure((int)((MetaMachine)this).Tier, Definition?.DataAccessCreative ?? false);
	}

	public void Configure(int tier, bool isCreative)
	{
		Tier            = tier;
		IsCreativeHatch = isCreative;
		EnsureInventory();
	}

	private void EnsureInventory()
	{
		if (ImportItems != null) return;
		if (IsCreativeHatch)
		{
			ImportItems = new NotifiableItemStackHandler(0, IO.BOTH);
		}
		else
		{
			// Mirrors upstream's inner class (overrides onContentsChanged + insertItem).
			ImportItems = new RebuildingInventory(this, GetInventorySize());
		}
		Traits.Attach(ImportItems);
		Traits.RegisterPersistent("ImportItems", ImportItems);
	}

	private sealed class RebuildingInventory : NotifiableItemStackHandler
	{
		private readonly DataAccessHatchMachine _owner;
		public RebuildingInventory(DataAccessHatchMachine owner, int slots)
			: base(slots, IO.BOTH)
		{
			_owner = owner;
			SetFilter(stack => ResearchManager.IsStackDataItem(stack, owner.IsBoundToDataBank()));
		}
		public override void OnContentsChanged()
		{
			base.OnContentsChanged();
			_owner.RebuildData(_owner.IsBoundToDataBank());
		}
	}

	protected int GetInventorySize() => Tier switch
	{
		(int)VoltageTier.LuV => 16,
		(int)VoltageTier.EV  => 9,
		(int)VoltageTier.HV  => 4,
		_                    => 1,
	};

	private bool IsBoundToDataBank()
	{
		foreach (var controller in GetControllers())
			return controller is IDataBankController;
		return false;
	}

	public bool IsCreative() => IsCreativeHatch;

	public bool IsRecipeAvailable(GTRecipe recipe, ICollection<IDataAccessHatch> seen)
	{
		seen.Add(this);
		bool hasResearchCond = false;
		foreach (var c in recipe.Conditions)
			if (c is ResearchCondition) { hasResearchCond = true; break; }
		if (!hasResearchCond) return true;
		return _recipes.Contains(recipe);
	}

	public int CountVisibleResearch(ICollection<IDataAccessHatch> seen)
	{
		if (seen.Contains(this)) return 0;
		seen.Add(this);
		if (IsCreativeHatch) return int.MaxValue;
		// Server-synced; _recipes is empty on clients.
		return _syncResearchCount;
	}

	// MUST inline the DIM body - NEVER `((IDataAccessHatch)this).ModifyRecipe`.
	// MultiblockPartMachine.ModifyRecipe shares the interface signature, so this
	// class member SHADOWS the DIM (C# uses a DIM only when no class member
	// implements it). An interface-cast call would self-recurse -> tail-call loop
	// -> silent server hang. Same trap as OpticalDataHatchMachine.
	public override GTRecipe? ModifyRecipe(GTRecipe recipe)
	{
		if (IsCreative()) return recipe;
		if (IsRecipeAvailable(recipe, new HashSet<IDataAccessHatch> { this })) return recipe;
		return null;
	}

	public override void AddedToController(MultiblockControllerMachine controller)
	{
		RebuildData(controller is IDataBankController);
		base.AddedToController(controller);
	}

	// Upstream canShared = isCreative.
	public bool CanShared() => IsCreativeHatch;

	private void RebuildData(bool isDataBank)
	{
		if (IsCreativeHatch || ImportItems == null) return;
		var rebuilt = new HashSet<GTRecipe>();
		for (int i = 0; i < ImportItems.SlotCount; i++)
		{
			var stack = ImportItems.GetSlot(i);
			if (!ResearchManager.IsStackDataItem(stack, isDataBank)) continue;
			var researchData = ResearchManager.ReadResearchId(stack);
			if (researchData is { } rd)
			{
				var collection = ResearchManager.GetRecipesFor(rd.RecipeType, rd.ResearchId);
				foreach (var r in collection) rebuilt.Add(r);
			}
		}
		_recipes = rebuilt;
		_syncResearchCount = rebuilt.Count;
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["isCreative"] = IsCreativeHatch;
		tag["dahResearchCount"] = _syncResearchCount;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		IsCreativeHatch = tag.GetBool("isCreative");
		if (tag.ContainsKey("dahResearchCount")) _syncResearchCount = tag.GetInt("dahResearchCount");
		EnsureInventory();
		Traits.Load(tag);   // late-registration re-load; recipe set rebuilds on AddedToController.
	}
}
