#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Transfer;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// PORTED - port of
// com.gregtechceu.gtceu.api.machine.trait.NotifiableItemStackHandler.
//
// Trait that holds a slot-backed item buffer and acts as a recipe-handler
// for ItemRecipeCapability. The HandleRecipeInner walker consumes / produces
// items via the slot range, returning the un-handled remainder so the
// recipe-logic loop can move on to other handler instances (multiple
// handlers can split a recipe's inputs/outputs across the machine).
//
// Documented adaptations:
//   - Forge ItemStack / IItemHandlerModifiable -> Terraria Item /
//     our IItemHandlerModifiable (same shape, different ItemStack type).
//   - Direction -> IODirection at the export/import boundary. (Machines
//     don't have facings yet; the methods are kept for future cover work
//     but no current call site uses them.)
//   - KubeJS IngredientAction filter dropped (we have no KubeJS).
//   - @SaveField/@SyncToClient -> trait Save/Load through MachineTraitHolder.
public class NotifiableItemStackHandler
	: NotifiableRecipeHandlerTrait<Ingredient>, IItemHandlerModifiable
{
	public static readonly MachineTraitType<NotifiableItemStackHandler> TYPE = new(allowMultipleInstances: true);
	public override MachineTraitType TraitType => TYPE;

	public IO HandlerIO { get; }
	public IO CapabilityIO { get; }
	public CustomItemStackHandler Storage { get; }

	private bool _shouldSearchContent = true;
	public bool ShouldSearchContent
	{
		get => _shouldSearchContent;
		set => _shouldSearchContent = value;
	}

	private bool? _isEmpty;

	private bool _shouldDropInventoryInWorld = true;
	public bool ShouldDropInventoryInWorld
	{
		get => _shouldDropInventoryInWorld;
		set => _shouldDropInventoryInWorld = value;
	}

	// === Construction =======================================================
	// Mirrors upstream's three constructors. The factory-taking overload lets
	// subclasses inject a custom storage variant (filtered, slot-limit
	// override, etc.).

	public NotifiableItemStackHandler(int slots, IO handlerIO, IO capabilityIO,
	                                  Func<int, CustomItemStackHandler> storageFactory)
	{
		HandlerIO = handlerIO;
		CapabilityIO = capabilityIO;
		Storage = storageFactory(slots);
		Storage.OnContentsChangedAction = OnContentsChanged;
	}

	public NotifiableItemStackHandler(int slots, IO handlerIO, IO capabilityIO)
		: this(slots, handlerIO, capabilityIO, n => new CustomItemStackHandler(n)) { }

	public NotifiableItemStackHandler(int slots, IO handlerIO)
		: this(slots, handlerIO, handlerIO) { }

	public NotifiableItemStackHandler SetFilter(Predicate<Item> filter)
	{
		Storage.Filter = filter;
		return this;
	}

	public virtual void OnContentsChanged()
	{
		_isEmpty = null;
		NotifyListeners();
	}

	// === handleRecipeInner (verbatim port) ==================================
	//
	// Upstream's walker - single-pass over `left`, mutating each Ingredient's
	// remaining count as we consume/produce slots. Removes fully-handled
	// entries from `left` so the recipe-logic loop knows what's still
	// outstanding.
	//
	// IO direction must match this handler's handlerIO; otherwise return
	// `left` unchanged. For the wrong direction we don't even iterate.
	public override List<Ingredient>? HandleRecipeInner(IO io, GTRecipe recipe,
	                                                    List<Ingredient> left, bool simulate)
		=> HandleRecipe(io, recipe, left, simulate, HandlerIO, Storage);

	// Verbatim port of upstream's static handleRecipe(io, recipe, left,
	// simulate, handlerIO, storage). Public+static so other callers
	// (MinerLogic, future ItemRecipeHandler) can reuse without going through
	// a trait instance - mirrors upstream's note about ItemRecipeHandler.
	public static List<Ingredient>? HandleRecipe(IO io, GTRecipe recipe,
	                                              List<Ingredient> left, bool simulate,
	                                              IO handlerIO, CustomItemStackHandler storage)
	{
		if (io != handlerIO) return left;
		if (io != IO.IN && io != IO.OUT) return left.Count == 0 ? null : left;

		// Temporarily remove listener so we can broadcast the entire set of
		// transactions once.
		var listener = storage.OnContentsChangedAction;
		storage.OnContentsChangedAction = () => { };
		bool changed = false;

		// Visited[] tracks per-slot post-op state so simulation works without
		// mutating the storage, and so non-simulation runs avoid clobbering.
		var visited = new Item?[storage.GetSlots()];

		for (int idx = 0; idx < left.Count; )
		{
			var ingredient = left[idx];
			if (IsIngredientEmpty(ingredient)) { left.RemoveAt(idx); continue; }

			Item[] items;
			int amount;
			if (ingredient is IntProviderIngredient provider)
			{
				if (simulate)
				{
					var maxStack = provider.GetMaxSizeStack();
					var output = maxStack.Count > 0 ? maxStack[0] : new Item();
					items = new[] { output };
				}
				else
				{
					var got = provider.GetItems();
					if (got.Count == 0 || got[0].IsAir) { left.RemoveAt(idx); continue; }
					items = new Item[got.Count];
					for (int k = 0; k < items.Length; k++) items[k] = got[k];
				}
				amount = items[0].stack;
			}
			else
			{
				var got = ingredient.GetItems();
				if (got.Count == 0 || got[0].IsAir) { left.RemoveAt(idx); continue; }
				items = new Item[got.Count];
				for (int k = 0; k < items.Length; k++) items[k] = got[k];
				if (ingredient is SizedIngredient si) amount = si.Amount;
				else amount = items[0].stack;
			}

			for (int slot = 0; slot < storage.GetSlots(); slot++)
			{
				var current = visited[slot] ?? storage.GetStackInSlot(slot);
				int count = current.IsAir ? 0 : current.stack;

				if (io == IO.IN)
				{
					if (current.IsAir) continue;
					if (ingredient.Test(current))
					{
						var extracted = storage.Extract(slot, Math.Min(count, amount), simulate);
						if (!extracted.IsAir)
						{
							changed = true;
							var snapshot = extracted.Clone();
							snapshot.stack = count - extracted.stack;
							if (snapshot.stack <= 0) snapshot.TurnToAir();
							visited[slot] = snapshot;
							amount -= extracted.stack;
						}
					}
				}
				else // IO.OUT
				{
					var output = items[0].Clone();
					output.stack = amount;
					if (visited[slot] is null || SameTypeAs(visited[slot]!, output))
					{
						int slotLimit = storage.GetSlotLimit(slot);
						if (count < output.maxStack && count < slotLimit)
						{
							var remainder = storage.Insert(slot, output, simulate);
							int placed = amount - (remainder.IsAir ? 0 : remainder.stack);
							if (placed > 0)
							{
								changed = true;
								var snapshot = output.Clone();
								snapshot.stack = count + placed;
								visited[slot] = snapshot;
							}
							amount = remainder.IsAir ? 0 : remainder.stack;
						}
					}
				}

				if (amount <= 0)
				{
					left.RemoveAt(idx);
					goto continueOuter;
				}
			}

			// Didn't finish off this ingredient - mutate it so the next handler
			// sees the remaining count.
			if (amount > 0)
			{
				if (ingredient is SizedIngredient si) si.Amount = amount;
				else if (items.Length > 0) items[0].stack = amount;
			}
			idx++;
			continueOuter: ;
		}

		storage.OnContentsChangedAction = listener;
		if (changed && !simulate) listener();

		return left.Count == 0 ? null : left;
	}

	private static bool IsIngredientEmpty(Ingredient ing)
	{
		var items = ing.GetItems();
		return items.Count == 0 || items[0].IsAir;
	}

	private static bool SameTypeAs(Item a, Item b) => !a.IsAir && !b.IsAir && a.type == b.type;

	// === IRecipeHandler<Ingredient> surface =================================

	public override RecipeCapability<Ingredient> GetCapability() => ItemRecipeCapability.CAP;

	public override IO GetHandlerIO() => HandlerIO;

	public int GetSize() => Storage.GetSlots();
	public int GetSlots() => Storage.GetSlots();

	public override IReadOnlyList<object> GetContents()
	{
		var stacks = new List<object>();
		for (int i = 0; i < Storage.GetSlots(); i++)
		{
			var stack = Storage.GetStackInSlot(i);
			if (!stack.IsAir) stacks.Add(stack);
		}
		return stacks;
	}

	public override double GetTotalContentAmount()
	{
		long amount = 0;
		for (int i = 0; i < Storage.GetSlots(); i++)
		{
			var stack = Storage.GetStackInSlot(i);
			if (!stack.IsAir) amount += stack.stack;
		}
		return amount;
	}

	public bool IsEmpty()
	{
		if (_isEmpty is null)
		{
			_isEmpty = true;
			for (int i = 0; i < Storage.GetSlots(); i++)
			{
				if (!Storage.GetStackInSlot(i).IsAir) { _isEmpty = false; break; }
			}
		}
		return _isEmpty.Value;
	}

	// === IItemHandlerModifiable =============================================
	// canCapInput / canCapOutput in upstream guard external pipe access so a
	// "this handler is OUT-only" trait rejects inserts from pipes. Mirror.

	public int SlotCount => Storage.SlotCount;
	public Item GetSlot(int slot) => Storage.GetSlot(slot);
	// virtual: upstream's NotifiableItemStackHandler methods are overridable
	// (Java default). ObjectHolderHandler @Overrides getSlotLimit / isItemValid /
	// extractItem - so these MUST be virtual here for that verbatim override to bind
	// (a non-virtual method + subclass `new` is the documented C# method-hiding trap,
	// which silently no-ops the override through any base/interface reference).
	public virtual int GetSlotLimit(int slot) => Storage.GetSlotLimit(slot);
	public virtual bool IsItemValid(int slot, Item item) => Storage.IsItemValid(slot, item);
	public void SetSlot(int slot, Item item) => Storage.SetSlot(slot, item);

	public Item Insert(int slot, Item item, bool simulate)
	{
		if (!CanCapInput()) return item;
		return Storage.Insert(slot, item, simulate);
	}

	public Item InsertInternal(int slot, Item item, bool simulate) =>
		Storage.Insert(slot, item, simulate);

	public virtual Item Extract(int slot, int amount, bool simulate)
	{
		if (!CanCapOutput()) return new Item();
		return Storage.Extract(slot, amount, simulate);
	}

	public Item ExtractInternal(int slot, int amount, bool simulate) =>
		Storage.Extract(slot, amount, simulate);

	// CapabilityIO guards external pipe insert/extract - mirrors upstream's
	// canCapInput / canCapOutput on ICapabilityTrait.
	public bool CanCapInput()  => CapabilityIO == IO.IN  || CapabilityIO == IO.BOTH;
	public bool CanCapOutput() => CapabilityIO == IO.OUT || CapabilityIO == IO.BOTH;

	// === Lifecycle ==========================================================

	public override void OnMachineDestroyed()
	{
		if (_shouldDropInventoryInWorld && Machine is not null)
		{
			Storage.DropInventoryInWorld(Machine.Position.X, Machine.Position.Y);
		}
	}

	// === Persistence ========================================================

	public override void Save(TagCompound tag)
	{
		tag["storage"]                    = Storage.SerializeNBT();
		tag["isDistinct"]                 = IsDistinct;
		tag["shouldSearchContent"]        = _shouldSearchContent;
		tag["shouldDropInventoryInWorld"] = _shouldDropInventoryInWorld;
	}

	public override void Load(TagCompound tag)
	{
		if (tag.ContainsKey("storage"))                    Storage.DeserializeNBT(tag.Get<TagCompound>("storage"));
		if (tag.ContainsKey("isDistinct"))                 SetDistinct(tag.GetBool("isDistinct"));
		if (tag.ContainsKey("shouldSearchContent"))        _shouldSearchContent       = tag.GetBool("shouldSearchContent");
		if (tag.ContainsKey("shouldDropInventoryInWorld")) _shouldDropInventoryInWorld = tag.GetBool("shouldDropInventoryInWorld");
	}
}
