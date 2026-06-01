#nullable enable
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Utils;

// MP-correct "give item to a SPECIFIC player" helper (cheat / reward / bag /
// refund / chest-dump). The naive QuickSpawnItem world-drop has visible click->
// slot latency on MP clients + a brief in-world race window. Routing:
//   - Owning client (SP, or whoAmI == Main.myPlayer): Player.GetItem by-reference
//     (instant, dupe-safe, preserves per-stack GlobalItem state); overflow falls
//     through to QuickSpawnItem.
//   - Dedicated server -> remote player: QuickSpawnItem world drop (Item.NewItem
//     auto-syncs, vanilla grab picks it up).
public static class PlayerGive
{
	private static GetItemSettings GiveSettings => GetItemSettings.NPCEntityToPlayerInventorySettings;

	public static void Give(Player player, IEntitySource src, int itemType, int stack)
	{
		if (player is null || stack <= 0 || itemType <= 0) return;
		if (IsOwningClient(player))
		{
			var item = new Item();
			item.SetDefaults(itemType);
			item.stack = stack;
			GiveInstance(player, src, item);
			return;
		}
		player.QuickSpawnItem(src, itemType, stack);
	}

	// Insert a pre-built Item (preserves per-stack data). Consumed by-reference
	// on the GetItem path - pass a fresh instance to keep your own copy.
	public static void Give(Player player, IEntitySource src, Item item)
	{
		if (player is null || item is null || item.IsAir || item.stack <= 0) return;
		if (IsOwningClient(player))
		{
			GiveInstance(player, src, item);
			return;
		}
		player.QuickSpawnItem(src, item, item.stack);
	}

	private static void GiveInstance(Player player, IEntitySource src, Item item)
	{
		var overflow = player.GetItem(player.whoAmI, item, GiveSettings);
		if (overflow is not null && !overflow.IsAir && overflow.stack > 0)
			player.QuickSpawnItem(src, overflow, overflow.stack);
	}

	private static bool IsOwningClient(Player player)
		=> Main.netMode != NetmodeID.Server && player.whoAmI == Main.myPlayer;
}
