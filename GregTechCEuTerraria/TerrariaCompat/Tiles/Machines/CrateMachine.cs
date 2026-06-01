#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Port of com.gregtechceu.gtceu.common.machine.storage.CrateMachine.
// Bulk item-storage crate - an N-slot inventory (27 wooden .. 144 tungstensteel).
// The machine declares IItemHandler and forwards to the `inventory` trait (WTM
// pattern). Tape mechanic (verbatim): RMB with duct/basic tape -> "taped"; a
// taped crate keeps its inventory across break->re-place (rides the dropped
// item), an untaped one scatters its contents loose.
//
// Per-material, NOT per-tier (Tiered=false).
// DEVIATION: tape's player.isCrouching() gate dropped (no crouch in Terraria).
public sealed class CrateMachine : MetaMachine, IItemHandler
{
	public CrateMachine() { }
	public CrateMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "Crate";

	// Slot count from the definition - registerCrate's `inventorySize`.
	public int InventorySize => Definition?.Capacity ?? 27;

	// upstream `inventory` (NotifiableItemStackHandler)
	private NotifiableItemStackHandler? _inventory;
	public NotifiableItemStackHandler Inventory { get { EnsureTraits(); return _inventory!; } }

	private void EnsureTraits()
	{
		if (_inventory is not null) return;
		BindDefinition();
		_inventory = new NotifiableItemStackHandler(InventorySize, Api.Capability.Recipe.IO.BOTH);
		Traits.Attach(_inventory);
		Traits.RegisterPersistent("inventory", _inventory);
	}

	protected override void OnMachineLoaded()
	{
		base.OnMachineLoaded();
		EnsureTraits();
	}

	// CrateLayout binds through SlotGroup.Inventory (raw trait Storage.Stacks)
	// so SlotAction's ref-mutation writes straight into the trait.
	public override Item[]? GetSlotGroup(SlotGroup group) => group switch
	{
		SlotGroup.Inventory => Inventory.Storage.Stacks,
		_ => base.GetSlotGroup(group),
	};

	public override void NotifySlotGroupChanged(SlotGroup group)
	{
		if (group == SlotGroup.Inventory) Inventory.OnContentsChanged();
	}

	// IItemHandler - forwards to the `inventory` trait.
	public int SlotCount => Inventory.SlotCount;
	public Item GetSlot(int slot) => Inventory.GetSlot(slot);
	public Item Insert(int slot, Item item, bool simulate) => Inventory.Insert(slot, item, simulate);
	public Item Extract(int slot, int amount, bool simulate) => Inventory.Extract(slot, amount, simulate);
	public bool IsItemValid(int slot, Item item) => true;

	// upstream @SaveField @SyncToClient `isTaped`
	private bool _isTaped;
	public bool IsTaped => _isTaped;

	// CrateMachine.onUseWithItem taped branch. Server-authoritative; caller consumes the tape.
	public void ApplyTape()
	{
		if (_isTaped) return;
		_isTaped = true;
	}

	// Untaped scatter on break (upstream shouldDropInventoryInWorld(!isTaped)).
	// Taped crates keep contents via WritePortableData; MetaMachine.OnKill never
	// drops SlotGroup.Inventory, so the scatter is done here.
	public override void OnKill()
	{
		base.OnKill();
		if (!IsServer || _isTaped) return;
		var src  = new EntitySource_TileBreak(Position.X, Position.Y);
		var rect = new Rectangle(Position.X * 16, Position.Y * 16, Size.Width * 16, Size.Height * 16);
		foreach (var stack in Inventory.Storage.Stacks)
		{
			if (stack.IsAir) continue;
			Item.NewItem(src, rect, stack.Clone());
		}
	}

	// Portable data across break -> re-place (upstream IDropSaveMachine): the
	// inventory rides the dropped item ONLY when taped.
	public override void WritePortableData(TagCompound tag)
	{
		if (!_isTaped) return;
		tag["taped"] = true;
		tag["slots"] = SaveSlots();
	}

	public override void ReadPortableData(TagCompound tag)
	{
		if (!tag.GetBool("taped")) return;
		_isTaped = true;
		LoadSlots(tag);
	}

	// `inventory` trait rides Traits.Save/Load.
	public override void SaveData(TagCompound tag)
	{
		EnsureTraits();
		base.SaveData(tag);
		tag["taped"] = _isTaped;
	}

	public override void LoadData(TagCompound tag)
	{
		EnsureTraits();
		base.LoadData(tag);
		_isTaped = tag.GetBool("taped");
	}

	// Inventory snapshot for the portable-data blob ({i, it} per non-air slot).
	private List<TagCompound> SaveSlots()
	{
		var list = new List<TagCompound>();
		var stacks = Inventory.Storage.Stacks;
		for (int i = 0; i < stacks.Length; i++)
			if (!stacks[i].IsAir)
				list.Add(new TagCompound { ["i"] = i, ["it"] = ItemIO.Save(stacks[i]) });
		return list;
	}

	private void LoadSlots(TagCompound tag)
	{
		if (!tag.ContainsKey("slots")) return;
		var stacks = Inventory.Storage.Stacks;
		for (int i = 0; i < stacks.Length; i++) stacks[i] = new Item();
		foreach (var entry in tag.GetList<TagCompound>("slots"))
		{
			int i = entry.GetInt("i");
			if (i >= 0 && i < stacks.Length)
				stacks[i] = ItemIO.Load(entry.GetCompound("it"));
		}
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		int used = 0;
		foreach (var s in Inventory.Storage.Stacks) if (!s.IsAir) used++;
		lines.Add($"Storage: {used} / {InventorySize} slots used");
		lines.Add(_isTaped ? "Taped - keeps contents when broken"
		                   : "Right-click with duct / basic tape to seal it");
	}
}
