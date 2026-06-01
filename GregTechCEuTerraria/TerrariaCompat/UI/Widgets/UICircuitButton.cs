#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Items;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Port of GhostCircuitSlotWidget. Backing storage is the machine's real
// `circuitInventory[0]` NotifiableItemStackHandler - empty is distinct from
// value 0 (empty = no item; IntCircuitIngredient won't match).
//   LMB increment / RMB decrement (wraps via empty); Shift+RMB clear; scroll cycles.
//
// Not ported: Shift+LMB picker popup (CircuitFancyConfigurator), the config-
// gated ghost vs slot toggle. We always run ghost mode (no drag-drop).
public sealed class UICircuitButton : UIElement
{
	public const int NoConfig = -1;   // sentinel - empty slot

	public const int MaxCircuit = IntCircuitItem.CircuitMax;   // 32

	private readonly NotifiableItemStackHandler _circuitInventory;
	private readonly Action<int> _send;

	private static Asset<Texture2D>? _slotBg;
	private static Asset<Texture2D>? _slotOverlay;
	private static readonly Asset<Texture2D>?[] _circuitByValue = new Asset<Texture2D>?[MaxCircuit + 1];

	public UICircuitButton(NotifiableItemStackHandler circuitInventory, Action<int> send, int width = 22, int height = 22)
	{
		_circuitInventory = circuitInventory;
		_send = send;
		Width = StyleDimension.FromPixels(width);
		Height = StyleDimension.FromPixels(height);
	}

	private static void EnsureAssets()
	{
		_slotBg      ??= ModContent.Request<Texture2D>("GregTechCEuTerraria/Content/Textures/gui/base/slot");
		_slotOverlay ??= ModContent.Request<Texture2D>("GregTechCEuTerraria/Content/Textures/gui/overlay/int_circuit_overlay");
	}

	private static Texture2D? CircuitSprite(int value)
	{
		// Upstream off-by-one: value N -> file (N+1).png (files 1..33 = 0..32).
		if (value < 0 || value > MaxCircuit) return null;
		int fileIndex = value + 1;
		_circuitByValue[value] ??= ModContent.Request<Texture2D>(
			$"GregTechCEuTerraria/Content/Textures/item/programmed_circuit/{fileIndex}");
		return _circuitByValue[value]?.Value;
	}

	// Mirrors IntCircuitBehaviour.getCircuitConfiguration; distinguishes empty
	// from configuration=0.
	private int CurrentValue()
	{
		if (_circuitInventory.SlotCount == 0) return NoConfig;
		var s = _circuitInventory.GetSlot(0);
		if (s == null || s.IsAir) return NoConfig;
		return (s.ModItem is IntCircuitItem ic) ? ic.Configuration : NoConfig;
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		EnsureAssets();
		var bounds = GetDimensions().ToRectangle();
		int value = CurrentValue();
		Texture2D? sprite = value == NoConfig ? _slotOverlay?.Value : CircuitSprite(value);

		PointClampDraw.Draw(spriteBatch, () =>
		{
			if (_slotBg?.Value is { } slotTex)
				spriteBatch.Draw(slotTex, bounds, Color.White);
			if (sprite != null)
			{
				int inset = System.Math.Max(2, bounds.Width / 9);
				var inner = new Rectangle(
					bounds.X + inset, bounds.Y + inset,
					bounds.Width - inset * 2, bounds.Height - inset * 2);
				var tint = IsMouseHovering ? Color.White : new Color(235, 235, 235);
				spriteBatch.Draw(sprite, inner, tint);
			}
		});

		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			Main.instance.MouseText(value == NoConfig
				? "Circuit: empty\nL-click cycle next\nR-click cycle prev\nShift+R-click: clear"
				: $"Circuit: {value}\nL-click +1, R-click -1\nShift+R-click: clear");
		}
	}

	public override void LeftMouseDown(UIMouseEvent evt)
	{
		base.LeftMouseDown(evt);
		Send(GetNextValue(increment: true));
		Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
	}

	public override void RightMouseDown(UIMouseEvent evt)
	{
		base.RightMouseDown(evt);
		bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
		if (shift)
		{
			Send(NoConfig);
		}
		else
		{
			Send(GetNextValue(increment: false));
		}
		Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
	}

	public override void ScrollWheel(UIScrollWheelEvent evt)
	{
		base.ScrollWheel(evt);
		if (evt.ScrollWheelValue == 0) return;
		Send(GetNextValue(increment: evt.ScrollWheelValue > 0));
		Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
	}

	// Verbatim port of GhostCircuitSlotWidget.getNextValue (lines 81-106).
	// Wrap goes via empty; 0 is skipped (since empty already represents "off").
	private int GetNextValue(bool increment)
	{
		int currentValue = CurrentValue();
		if (increment)
		{
			if (currentValue == MaxCircuit) return NoConfig;
			if (currentValue == NoConfig) return 1;
			return currentValue + 1;
		}
		else
		{
			if (currentValue == NoConfig) return MaxCircuit;
			if (currentValue == 1) return NoConfig;
			return currentValue - 1;
		}
	}

	private void Send(int target) => _send(target);
}
