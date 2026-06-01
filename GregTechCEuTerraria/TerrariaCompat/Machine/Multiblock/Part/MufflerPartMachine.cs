#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Transfer;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of MufflerPartMachine. Listens for AfterWorking, rolls per-item chance
// to drop recovery items (slag/ash) into its (tier+1)^2 inventory at max(1, tier*10)%.
//
// Environmental hazard subsystem dropped (whole hazard pipeline unported).
// isFrontFaceFree veto dropped (no facing). tryBreakSnow N/A. Particles ported
// via Terraria Dust - vent straight up since we have no facing.
public class MufflerPartMachine : TieredPartMachine
{
	protected override string Label => "Muffler Hatch";

	public int RecoveryChance { get; protected set; }
	public CustomItemStackHandler? Inventory { get; protected set; }

	public override Api.Capability.IItemHandler? ExposedItemHandler => Inventory;

	private static readonly Random Rng = new();

	public MufflerPartMachine() : base() { }

	public void Configure(int tier)
	{
		Tier           = tier;
		RecoveryChance = Math.Max(1, tier * 10);
		EnsureInventory();
	}

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		if (Definition == null) return;
		Configure((int)((MetaMachine)this).Tier);
	}

	public override Item[]? GetSlotGroup(TerrariaCompat.Machine.SlotGroup group) =>
		group == TerrariaCompat.Machine.SlotGroup.Inventory && Inventory != null
			? Inventory.Stacks
			: base.GetSlotGroup(group);

	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		int size = Inventory?.GetSlots() ?? (Tier + 1) * (Tier + 1);
		lines.Add($"Recovery Chance: {RecoveryChance}%");
		lines.Add($"Capacity: {size} slots");
	}

	private void EnsureInventory()
	{
		if (Inventory != null) return;
		int size = (Tier + 1) * (Tier + 1);
		Inventory = new CustomItemStackHandler(size);
	}

	// Per-item independent roll; spillover dropped (upstream discards too).
	public void RecoverItemsTable(params Item[] recoveryItems)
	{
		if (Inventory == null) return;
		int numRolls = Math.Min(recoveryItems.Length, Inventory.GetSlots());
		for (int i = 0; i < numRolls; i++)
		{
			if (!CalculateChance()) continue;
			var copy = recoveryItems[i].Clone();
			InsertStacked(Inventory, copy);
		}
	}

	private bool CalculateChance() =>
		RecoveryChance >= 100 || RecoveryChance >= Rng.Next(100);

	// Mirrors Forge ItemHandlerHelper.insertItemStacked: merge into matching
	// stacks first, then empty slots; spillover dropped.
	private static void InsertStacked(CustomItemStackHandler handler, Item stack)
	{
		if (stack == null || stack.IsAir) return;
		for (int i = 0; i < handler.GetSlots(); i++)
		{
			var existing = handler.GetSlot(i);
			if (existing.IsAir || existing.type != stack.type) continue;
			var remaining = handler.Insert(i, stack, simulate: false);
			if (remaining.IsAir || remaining.stack <= 0) return;
			stack = remaining;
		}
		for (int i = 0; i < handler.GetSlots(); i++)
		{
			if (!handler.GetSlot(i).IsAir) continue;
			var remaining = handler.Insert(i, stack, simulate: false);
			if (remaining.IsAir || remaining.stack <= 0) return;
			stack = remaining;
		}
	}

	public override bool AfterWorking(IWorkableMultiController controller)
	{
		// Hazard emission would go here - see header.
		var factory = controller.Self().Definition?.RecoveryItemsFactory;
		if (factory != null)
		{
			var items = factory.Invoke();
			if (items != null && items.Length > 0)
				RecoverItemsTable(items);
		}
		return true;
	}

	// 2D adapt: vent straight up. Per-frame 3-puff burst gives a billowing look.
	public override void OnClientFrame()
	{
		if (!IsFormed()) return;

		bool working = false;
		foreach (var ctrl in GetControllers())
		{
			if (ctrl is IRecipeLogicMachine rlm && rlm.GetRecipeLogic().IsWorking())
			{
				working = true;
				break;
			}
		}
		if (!working) return;

		// Top edge of the 2x2 footprint; 2-3 puffs/frame, noGravity carries far.
		int puffs = 2 + Main.rand.Next(2);
		for (int i = 0; i < puffs; i++)
		{
			float x = Position.X * 16f + Main.rand.NextFloat(0f, 32f);
			float y = Position.Y * 16f + Main.rand.NextFloat(-4f, 2f);

			var dust = Dust.NewDustPerfect(
				new Microsoft.Xna.Framework.Vector2(x, y),
				DustID.Smoke,
				new Microsoft.Xna.Framework.Vector2(
					Main.rand.NextFloat(-0.8f, 0.8f),
					Main.rand.NextFloat(-5.5f, -3.0f)),
				Alpha: 100,
				Scale: Main.rand.NextFloat(1.4f, 2.2f));
			dust.noGravity = true;
			dust.noLight   = true;
			dust.fadeIn    = 0.6f;
		}
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		if (Inventory != null)
			tag["inventory"] = Inventory.SerializeNBT();
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		RecoveryChance = Math.Max(1, Tier * 10);
		EnsureInventory();
		if (tag.ContainsKey("inventory"))
			Inventory!.DeserializeNBT(tag.GetCompound("inventory"));
	}
}
