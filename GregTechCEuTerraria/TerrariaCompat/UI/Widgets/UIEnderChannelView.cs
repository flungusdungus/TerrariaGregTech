#nullable enable
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Cover.Ender;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Read-only view of a virtual ender channel's buffered contents, for the
// ender-cover settings popup. Resolves the live channel from
// VirtualEnderRegistry each frame - server-live in SP, a sync mirror on an MP
// client (populated by EnderChannelSyncPacket). The channel is filled /
// drained by the ender covers themselves; this widget never mutates it.
//
// Draws an item slot for an item channel, a horizontal fill bar for a fluid
// channel - picked off the cover's EntryType. A redstone link has no contents
// view and never gets one of these.
public sealed class UIEnderChannelView : UIElement
{
	private const float VanillaNativeSlotPixels = 52f;

	private readonly ICoverable _entity;
	private readonly CoverSide _side;
	private readonly Item[] _itemRender = { new() };

	public UIEnderChannelView(ICoverable entity, CoverSide side)
	{
		_entity = entity;
		_side = side;
	}

	protected override void DrawSelf(SpriteBatch sb)
	{
		if (_entity.GetCoverAtSide(_side) is not IEnderLinkCover cover) return;
		var entry = VirtualEnderRegistry.Instance.GetEntry(cover.EntryType, cover.ChannelName);
		var bounds = GetDimensions().ToRectangle();

		if (cover.EntryType == EnderEntryType.Item)
			DrawItem(sb, bounds, entry as VirtualItemStorage);
		else if (cover.EntryType == EnderEntryType.Fluid)
			DrawFluid(sb, bounds, entry as VirtualTank);
	}

	private void DrawItem(SpriteBatch sb, Rectangle bounds, VirtualItemStorage? storage)
	{
		_itemRender[0] = storage is not null && !storage.IsEmpty()
			? storage.Handler.GetSlot(0)
			: new Item();

		float oldScale = Main.inventoryScale;
		Main.inventoryScale = bounds.Width / VanillaNativeSlotPixels;
		try
		{
			if (IsMouseHovering)
			{
				Main.LocalPlayer.mouseInterface = true;
				ItemSlot.MouseHover(_itemRender, ItemSlot.Context.ChestItem, 0);
			}
			ItemSlot.Draw(sb, _itemRender, ItemSlot.Context.ChestItem, 0, new Vector2(bounds.X, bounds.Y));
		}
		finally
		{
			Main.inventoryScale = oldScale;
		}
	}

	private void DrawFluid(SpriteBatch sb, Rectangle bounds, VirtualTank? tank)
	{
		var tex = TextureAssets.MagicPixel.Value;
		sb.Draw(tex, bounds, new Color(25, 30, 50) * 0.9f);

		FluidStack stored = tank?.FluidTank.Fluid ?? FluidStack.Empty;
		int capacity = tank?.FluidTank.Capacity ?? VirtualTank.DefaultCapacity;

		if (!stored.IsEmpty && capacity > 0)
		{
			float fill = System.Math.Clamp((float)stored.Amount / capacity, 0f, 1f);
			int fillW = (int)(bounds.Width * fill);
			if (fillW > 0)
			{
				var fillRect = new Rectangle(bounds.X, bounds.Y, fillW, bounds.Height);
				if (!FluidIconRenderer.Draw(sb, stored.Type!, fillRect))
					sb.Draw(tex, fillRect, FluidIconRenderer.RgbColor(stored.Type!.Color));
			}
		}

		TankFrame.DrawBorder(sb, bounds, IsMouseHovering
			? Color.Lerp(TankFrame.BorderColor, Color.White, 0.5f)
			: TankFrame.BorderColor);

		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			Main.LocalPlayer.cursorItemIconEnabled = false;
			Main.instance.MouseText(stored.IsEmpty
				? $"Channel empty  (0 / {capacity:N0} mB)"
				: $"{stored.Type!.DisplayName}: {stored.Amount:N0} / {capacity:N0} mB");
		}
	}
}
