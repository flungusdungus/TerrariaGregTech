#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Capabilities.Handlers;

// IItemHandler over a vanilla Terraria chest, addressed by chest index.
//
// This is the Terraria analogue of upstream's Forge ITEM_HANDLER capability on
// a vanilla MC container - it lets a conveyor cover (and, later, item pipes)
// move items in and out of an adjacent chest.
//
// Server-authority rule: while a player has the chest OPEN it is no longer
// server-authoritative (the viewing client mutates the chest locally and the
// game syncs slot-by-slot), so the handler FREEZES - Insert returns the stack
// untouched and Extract returns empty while the chest is in use. The conveyor
// simply transfers nothing that tick. Outside of that the handler is fully
// consistent; every non-simulated mutation is broadcast with SyncChestItem so
// MP clients stay in step.
public sealed class VanillaChestItemHandler : IItemHandler
{
	private readonly int _chestIndex;

	private VanillaChestItemHandler(int chestIndex) => _chestIndex = chestIndex;

	// Resolve the chest occupying tile (x,y) - works for any cell of the 2x2
	// chest footprint by walking back to the top-left via the tile frame.
	public static IItemHandler? At(int x, int y)
	{
		if (x < 1 || y < 1 || x >= Main.maxTilesX - 1 || y >= Main.maxTilesY - 1) return null;
		Tile tile = Main.tile[x, y];
		if (!tile.HasTile || !Main.tileContainer[tile.TileType]) return null;

		int left = x - (tile.TileFrameX % 36) / 18;
		int top = y - (tile.TileFrameY % 36) / 18;
		int idx = Chest.FindChest(left, top);
		return idx >= 0 ? new VanillaChestItemHandler(idx) : null;
	}

	private Chest? TheChest =>
		_chestIndex >= 0 && _chestIndex < Main.chest.Length ? Main.chest[_chestIndex] : null;

	// While any player has this chest open it is not server-authoritative.
	private bool Locked
	{
		get
		{
			if (TheChest?.item is null) return true;
			for (int p = 0; p < Main.maxPlayers; p++)
				if (Main.player[p].active && Main.player[p].chest == _chestIndex)
					return true;
			return false;
		}
	}

	public int SlotCount => TheChest?.item.Length ?? 0;

	public Item GetSlot(int slot) => TheChest?.item[slot] ?? new Item();

	public Item Insert(int slot, Item item, bool simulate)
	{
		if (item is null || item.IsAir) return new Item();
		if (Locked) return item.Clone();

		var chest = TheChest!;
		var existing = chest.item[slot];

		if (existing is null || existing.IsAir)
		{
			int accept = Math.Min(item.stack, item.maxStack);
			var leftover = item.Clone();
			leftover.stack = item.stack - accept;
			if (leftover.stack <= 0) leftover.TurnToAir();
			if (!simulate)
			{
				var placed = item.Clone();
				placed.stack = accept;
				chest.item[slot] = placed;
				Sync(slot);
			}
			return leftover;
		}

		if (existing.type != item.type) return item.Clone();

		int room = Math.Min(existing.maxStack, item.maxStack) - existing.stack;
		if (room <= 0) return item.Clone();

		int merged = Math.Min(room, item.stack);
		var lo = item.Clone();
		lo.stack = item.stack - merged;
		if (lo.stack <= 0) lo.TurnToAir();
		if (!simulate)
		{
			existing.stack += merged;
			Sync(slot);
		}
		return lo;
	}

	public Item Extract(int slot, int maxAmount, bool simulate)
	{
		if (maxAmount <= 0 || Locked) return new Item();

		var chest = TheChest!;
		var existing = chest.item[slot];
		if (existing is null || existing.IsAir) return new Item();

		int take = Math.Min(existing.stack, maxAmount);
		var taken = existing.Clone();
		taken.stack = take;
		if (!simulate)
		{
			existing.stack -= take;
			if (existing.stack <= 0) chest.item[slot] = new Item();
			Sync(slot);
		}
		return taken;
	}

	private void Sync(int slot)
	{
		if (Main.netMode == NetmodeID.Server)
			NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, _chestIndex, slot);
	}
}
