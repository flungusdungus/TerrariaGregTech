#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using Terraria.UI.Chat;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Deposit/extract slot for SuperChestTileEntity. Holds one item type up to
// billions of units; icon shows the type, overlay shows the real count.
// Clicks route through ChestAction (server-authoritative; uses
// CursorUpdatePacket - bypasses SyncEquipment's ignore-self dupe gate).
// LMB+cursor = Insert; LMB empty = Dump one Item.maxStack.
public sealed class UISuperChestSlot : UIElement
{
	private readonly SuperChestTileEntity _chest;
	private const float VanillaNativeSlotPixels = 52f;

	private static readonly Item[] _temp = { new() };

	public UISuperChestSlot(SuperChestTileEntity chest, int sizePx)
	{
		_chest = chest;
		Width  = StyleDimension.FromPixels(sizePx);
		Height = StyleDimension.FromPixels(sizePx);
	}

	public override void LeftMouseDown(UIMouseEvent evt)
	{
		base.LeftMouseDown(evt);
		if (!Main.mouseItem.IsAir)
		{
			TerrariaCompat.Net.Actions.MachineActions.Send(
				new ChestAction(ChestAction.Op.Insert, Main.mouseItem), _chest);
			SoundEngine.PlaySound(SoundID.Grab);
		}
		else if (!_chest.StoredItem.IsAir && _chest.StoredAmount > 0)
		{
			TerrariaCompat.Net.Actions.MachineActions.Send(
				new ChestAction(ChestAction.Op.Dump, true), _chest);
			SoundEngine.PlaySound(SoundID.Grab);
		}
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var bounds = GetDimensions().ToRectangle();

		// stack=1 so vanilla's stack-count overlay doesn't render the wrong number.
		var stored = _chest.StoredItem;
		_temp[0] = stored.IsAir ? new Item() : stored.Clone();
		if (!_temp[0].IsAir) _temp[0].stack = 1;

		float oldScale = Main.inventoryScale;
		Main.inventoryScale = bounds.Width / VanillaNativeSlotPixels;
		try
		{
			if (IsMouseHovering)
			{
				Main.LocalPlayer.mouseInterface = true;
				if (!_temp[0].IsAir)
				{
					ItemSlot.OverrideHover(_temp, ItemSlot.Context.ChestItem, 0);
					ItemSlot.MouseHover(_temp, ItemSlot.Context.ChestItem, 0);
				}
			}
			ItemSlot.Draw(spriteBatch, _temp, ItemSlot.Context.ChestItem, 0,
				new Vector2(bounds.X, bounds.Y));

			// Real count overlay in slot-stack font (truncated: 1.2M / 14.5k).
			if (!_temp[0].IsAir && _chest.StoredAmount > 0)
			{
				string text = FormatCount(_chest.StoredAmount);
				DynamicSpriteFont font = FontAssets.ItemStack.Value;
				const float overlayScale = 0.7f;
				var size = ChatManager.GetStringSize(font, text, new Vector2(overlayScale));
				var pos = new Vector2(
					bounds.Right - size.X - 4,
					bounds.Bottom - size.Y - 2);
				ChatManager.DrawColorCodedStringWithShadow(
					spriteBatch, font, text, pos, Color.White, 0f,
					Vector2.Zero, new Vector2(overlayScale));
			}
		}
		finally
		{
			Main.inventoryScale = oldScale;
		}
	}

	private static string FormatCount(long n)
	{
		if (n >= 1_000_000_000) return (n / 1_000_000_000.0).ToString("0.#") + "B";
		if (n >= 1_000_000)     return (n / 1_000_000.0).ToString("0.#") + "M";
		if (n >= 10_000)        return (n / 1_000.0).ToString("0.#") + "k";
		return n.ToString();
	}
}
