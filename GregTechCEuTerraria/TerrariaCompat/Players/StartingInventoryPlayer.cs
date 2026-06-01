#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Items;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Players;

// Gives every new character a Steam Age Skip Bag - right-click to open and
// receive the LV-age starter kit (see SteamAgeSkipBag for contents). Mirrors
// vanilla's copper-tools starting inventory - fires once per character at
// creation (and on mediumcore death-respawn, same as copper tools).
// Per-character, not per-world: the player carries it across worlds, just
// like the copper pickaxe. The Too Many Items browser is still reachable via
// the inventory button (third icon on the Sort / QuickStack row), so the
// debug block doesn't need to be in the starting inventory anymore.
public sealed class StartingInventoryPlayer : ModPlayer
{
	public override IEnumerable<Item> AddStartingItems(bool mediumCoreDeath)
	{
		var tools = new Item();
		tools.SetDefaults(ModContent.ItemType<GregTechIronToolsBag>());
		yield return tools;

		var bag = new Item();
		bag.SetDefaults(ModContent.ItemType<SteamAgeSkipBag>());
		yield return bag;

		var stoneBag = new Item();
		stoneBag.SetDefaults(ModContent.ItemType<SkipStoneAgeBag>());
		yield return stoneBag;
	}
}
