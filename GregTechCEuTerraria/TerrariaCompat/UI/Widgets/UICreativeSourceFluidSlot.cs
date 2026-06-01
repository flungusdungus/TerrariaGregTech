#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Phantom fluid slot for the Creative Tank source-fluid setter. Click with a
// held container item (bucket / fluid cell) whose IFluidHandlerItem can be
// read -> setter fires with the contained fluid type. Right-click clears.
//
// Drawing follows the UIFluidSlot convention: a 22x22 vanilla chest-slot
// frame with the fluid-icon overlay centered inside.
public sealed class UICreativeSourceFluidSlot : UIElement
{
	private const int Native = 22;

	private readonly Func<FluidType?> _getter;
	private readonly Action<FluidType?> _setter;

	public UICreativeSourceFluidSlot(Func<FluidType?> getter, Action<FluidType?> setter)
	{
		_getter = getter;
		_setter = setter;
		Width  = StyleDimension.FromPixels(Native);
		Height = StyleDimension.FromPixels(Native);
	}

	public override void LeftMouseDown(UIMouseEvent evt)
	{
		base.LeftMouseDown(evt);
		var cursor = Main.mouseItem;
		FluidType? type = null;
		if (cursor is not null && !cursor.IsAir)
		{
			if (cursor.ModItem is IFluidHandlerItem fhi)
			{
				var stack = fhi.GetTank(0);
				if (!stack.IsEmpty) type = stack.Type;
			}
			else
			{
				var stack = VanillaFluidBridge.StackFor(cursor.type);
				if (!stack.IsEmpty) type = stack.Type;
			}
		}
		_setter(type);
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	public override void RightMouseDown(UIMouseEvent evt)
	{
		base.RightMouseDown(evt);
		_setter(null);
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	protected override void DrawSelf(SpriteBatch sb)
	{
		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			var t = _getter();
			Main.instance.MouseText(t is null ? "Click with a bucket / fluid cell to set source" : t.DisplayName);
		}
		var bounds = GetDimensions().ToRectangle();
		// Vanilla chest-slot frame (Inventory_Back asset).
		var slotTex = TextureAssets.InventoryBack.Value;
		sb.Draw(slotTex, bounds, Color.White * 0.85f);
		var fluid = _getter();
		if (fluid is not null)
		{
			var inner = new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6);
			FluidIconRenderer.Draw(sb, fluid, inner, light: Color.White);
		}
	}
}
