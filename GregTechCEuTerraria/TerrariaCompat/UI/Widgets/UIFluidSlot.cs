#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Vertical fill column accepting RMB bucket / fluid-cell transfers. Per-tank
// version of SuperTankTile.RightClick so multi-tank machines route each
// click to the right slot.
public sealed class UIFluidSlot : UIElement
{
	private readonly MetaMachine _entity;
	private readonly IFluidHandler _handler;
	private readonly int _tankIndex;
	private readonly bool _allowFill;
	private readonly bool _allowDrain;
	private bool _rightDown;

	// localTankIndex is local to `direction` - machine resolves to flat handler
	// index. AllowFill/AllowDrain come from GetTankClickCaps (same source the
	// server-side FluidSlotAction reads) so UI + server can't disagree.
	public UIFluidSlot(MetaMachine entity, IO direction, int localTankIndex, int width, int height)
	{
		_entity = entity;
		_handler = (IFluidHandler)entity;
		_tankIndex = entity.ResolveFluidTank(direction, localTankIndex);
		(_allowFill, _allowDrain) = _handler.GetTankClickCaps(_tankIndex);
		Width = StyleDimension.FromPixels(width);
		Height = StyleDimension.FromPixels(height);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var bounds = GetDimensions().ToRectangle();
		var tex = TextureAssets.MagicPixel.Value;
		var stored = _handler.GetTank(_tankIndex);
		int capacity = _handler.GetCapacity(_tankIndex);

		spriteBatch.Draw(tex, bounds, new Color(25, 30, 50) * 0.9f);

		// Fluid column rises from the bottom.
		if (!stored.IsEmpty && capacity > 0)
		{
			float fill = System.Math.Clamp((float)stored.Amount / capacity, 0f, 1f);
			int fillH = (int)(bounds.Height * fill);
			if (fillH > 0)
			{
				var fillRect = new Rectangle(bounds.X, bounds.Y + bounds.Height - fillH, bounds.Width, fillH);
				if (!FluidIconRenderer.Draw(spriteBatch, stored.Type!, fillRect))
					spriteBatch.Draw(tex, fillRect, FluidIconRenderer.RgbColor(stored.Type!.Color));
			}
		}

		var border = IsMouseHovering
			? Color.Lerp(TankFrame.BorderColor, Color.White, 0.5f)
			: TankFrame.BorderColor;
		TankFrame.DrawBorder(spriteBatch, bounds, border);

		if (IsMouseHovering && !MachineUISystem.IsOccludedByHigherModal)
		{
			Main.LocalPlayer.mouseInterface = true;
			Main.LocalPlayer.cursorItemIconEnabled = false;
			// Hint matches the allowed click direction.
			string hint =
				  _allowFill  && _allowDrain ? "R-click with a bucket to fill or empty"
				: _allowFill                 ? "R-click with a bucket to fill"
				: _allowDrain                ? "R-click with an empty bucket to drain"
				:                              "";
			string label = stored.IsEmpty
				? $"Empty  (0 / {capacity:N0} mB)\n{hint}"
				: $"{stored.Type!.DisplayName}: {stored.Amount:N0} / {capacity:N0} mB\n{hint}";
			Main.instance.MouseText(label);
			// Don't reset the browser hover filter on an empty tank.
			if (!stored.IsEmpty)
				HoverItemTracker.SetFluid(stored.Type!.Id);
			HandleClicks();
		}
		_rightDown = Main.mouseRight;
	}

	// Edge-triggered RMB; simulated outcome gates the splash + packet.
	private void HandleClicks()
	{
		if (!Main.mouseRight || _rightDown) return;
		// Cursor-only by design - bucket/cell must be on Main.mouseItem.
		var held = Main.mouseItem;
		if (held is null || held.IsAir) return;
		if (!WouldTransfer(held)) return;

		MachineActions.Send(new FluidSlotAction(_tankIndex, held), _entity);
		Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.Splash);
	}

	// Mirrors FluidSlotAction.Apply's accept rules - splash + packet gated
	// identically to the server-side transfer.
	private bool WouldTransfer(Item held)
	{
		var tank = _handler.GetTankAccess(_tankIndex);

		var vanilla = VanillaFluidBridge.StackFor(held.type);
		if (!vanilla.IsEmpty)
			return _allowFill && tank.Fill(vanilla, simulate: true) >= vanilla.Amount;

		if (held.ModItem is Items.Fluids.FluidBucketItem gtBucket && gtBucket.Fluid is { } gf)
			return _allowFill
				&& tank.Fill(new FluidStack(gf, VanillaFluidBridge.BucketAmount),
					simulate: true) >= VanillaFluidBridge.BucketAmount;

		// Empty bucket needs a filled-bucket form for the stored fluid.
		if (held.type == Terraria.ID.ItemID.EmptyBucket)
		{
			if (!_allowDrain) return false;
			var stored = tank.GetTank(0);
			if (stored.IsEmpty || stored.Amount < VanillaFluidBridge.BucketAmount) return false;
			return VanillaFluidBridge.FilledVersion(Terraria.ID.ItemID.EmptyBucket, stored.Type!) != 0
			    || Items.Fluids.FluidBucketRegistry.Get(stored.Type!.Id) != null;
		}

		if (held.ModItem is Items.Fluids.FluidCellItem cell)
		{
			if (cell.GetFluidStack().IsEmpty)
				return _allowDrain && !tank.Drain(cell.Capacity, simulate: true).IsEmpty;
			return _allowFill && tank.Fill(cell.GetFluidStack(), simulate: true) > 0;
		}

		return false;
	}
}
