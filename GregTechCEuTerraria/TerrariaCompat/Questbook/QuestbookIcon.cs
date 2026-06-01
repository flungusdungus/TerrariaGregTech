#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.Questbook;

// Draws an item icon via vanilla ItemSlot.Draw - same path as the recipe
// browser, machine UI, etc. Sizes the slot via Main.inventoryScale; vanilla
// handles layer compositing / animations / Item.Size.
internal static class QuestbookIcon
{
	private static readonly Item[] TempSlot = new Item[1];

	public static void Draw(SpriteBatch sb, int itemType, Vector2 center, float maxSize)
	{
		if (itemType <= 0)
			return;

		float prev = Main.inventoryScale;
		Main.inventoryScale = maxSize / 52f;     // 52 = vanilla slot native pixel width
		try
		{
			TempSlot[0] = new Item();
			TempSlot[0].SetDefaults(itemType);
			Vector2 pos = center - new Vector2(maxSize * 0.5f);
			ItemSlot.Draw(sb, TempSlot, ItemSlot.Context.CraftingMaterial, 0, pos, Color.White);
		}
		finally
		{
			Main.inventoryScale = prev;
		}
	}
}
