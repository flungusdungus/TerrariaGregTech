#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Routes player-inventory shift-clicks into the open machine's input slots
// (complement of UISlot.ShiftClickToPlayerInventory). Fires only when a machine
// UI is open, the slot is player inventory, and the entity is an IItemHandler.
//
// MP void bug: this fires client-side, and a client-side handler.Insert is
// overwritten by the next MachineStateSyncPacket - items leave the slot but
// never land. Fix: snapshot + clear the source slot locally (with SyncEquipment
// so the server's mirror reduces too) + dispatch SlotAction.ShiftClickIn; the
// server inserts authoritatively and returns leftover via CursorUpdatePacket.
public sealed class MachineShiftClickPlayer : ModPlayer
{
	public override bool ShiftClickSlot(Item[] inventory, int context, int slot)
	{
		if (!MachineUISystem.IsOpen) return false;
		var entity = MachineUISystem.CurrentEntity;
		if (entity is null || entity is not IItemHandler) return false;

		// Only handle plain player-inventory contexts. Other contexts (trash
		// can, equip slots, etc.) keep their vanilla shift-click meaning.
		if (context != ItemSlot.Context.InventoryItem
		    && context != ItemSlot.Context.InventoryCoin
		    && context != ItemSlot.Context.InventoryAmmo
		    && context != ItemSlot.Context.ChestItem)
			return false;

		if (slot < 0 || slot >= inventory.Length) return false;
		var src = inventory[slot];
		if (src.IsAir) return false;

		// Detach locally - can't keep the item while the round-trip is in flight
		// (a SP Apply would double-deposit: raw slot + GetItem leftover).
		var snapshot = src.Clone();
		inventory[slot].TurnToAir();
		// SyncEquipment so the server's inventory mirror drops it too, else a
		// later sync resurrects it on the client.
		if (Main.netMode == NetmodeID.MultiplayerClient)
			NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, Main.myPlayer, slot);

		// SlotGroup / index are unused by ShiftClickIn - the server scans
		// every accepting machine slot via IItemHandler.
		MachineActions.Send(
			new SlotAction(SlotGroup.Inventory, 0, SlotAction.Kind.ShiftClickIn, snapshot),
			(MetaMachine)entity);
		SoundEngine.PlaySound(SoundID.Grab);
		return true;
	}
}
