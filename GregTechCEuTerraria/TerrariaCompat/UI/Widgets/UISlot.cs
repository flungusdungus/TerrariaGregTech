#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Item slot - server-authoritative clicks via SlotAction (MagicStorage shape).
// Concurrent server mutation (auto-output, recipe finish) can't race the click
// into a dupe. Click plumbing is tML-native (LeftMouseDown/RightMouseDown
// press-edge events) - independent of Main.mouseLeftRelease and ModalEscape.
// We do NOT call ItemSlot.LeftClick/RightClick: those mutate slot + cursor
// locally and would lie about state the server hasn't blessed yet. MP cost:
// one RTT per click. Vanilla Draw / MouseHover / OverrideHover remain safe
// (render + hover only, no mutation).
public sealed class UISlot : UIElement
{
	public const int NativeUnscaledSize = 22;
	private const float VanillaNativeSlotPixels = 52f;

	private readonly MetaMachine _entity;
	private readonly SlotGroup _group;
	private readonly int _slotIndex;
	private readonly int _context;
	// Mirror of upstream SlotWidget's canPutItems=false on output handlers.
	private readonly bool _isOutput;
	// Port of upstream BlockableSlotWidget.setIsBlocked - rotor holder uses
	// this to keep a spinning rotor from being yanked mid-tick.
	private readonly System.Func<bool>? _isBlocked;
	// Upstream `setBackground(SLOT, EXTRA)` second layer - rotor holder's
	// TURBINE_OVERLAY is the canonical user.
	private readonly string? _emptyOverlayAsset;
	private ReLogic.Content.Asset<Texture2D>? _emptyOverlayTex;

	// Render-only reference; server mutates independently, UI reflects last sync.
	private readonly Item[]? _slotsForRender;

	public UISlot(MetaMachine entity, SlotGroup group, int slotIndex,
		int context = ItemSlot.Context.ChestItem,
		System.Func<bool>? isBlocked = null,
		string? emptyOverlayAsset = null)
	{
		_entity = entity;
		_group = group;
		_slotIndex = slotIndex;
		_context = context;
		_isOutput = group == SlotGroup.InventoryOutput;
		_isBlocked = isBlocked;
		_emptyOverlayAsset = emptyOverlayAsset;
		_slotsForRender = entity.GetSlotGroup(group);
		Width = StyleDimension.FromPixels(NativeUnscaledSize);
		Height = StyleDimension.FromPixels(NativeUnscaledSize);
	}

	// 1-element scratch - UI draw is synchronous so one shared buffer is safe.
	private static readonly Item[] _temp = { new() };

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		if (_slotsForRender is null || _slotIndex >= _slotsForRender.Length) return;

		var bounds = GetDimensions().ToRectangle();

		// Always draw at index 0: vanilla's gamepad-nav + context arrays are
		// sized for chest (40) / inventory (58), so a real 144-slot crate
		// index overflows them. Click handlers still use the real array+index.
		_temp[0] = _slotsForRender[_slotIndex];

		float oldScale = Main.inventoryScale;
		Main.inventoryScale = bounds.Width / VanillaNativeSlotPixels;
		try
		{
			if (IsMouseHovering)
			{
				// Clicks live in LeftMouseDown / RightMouseDown.
				Main.LocalPlayer.mouseInterface = true;
				ItemSlot.OverrideHover(_temp, _context, 0);
				ItemSlot.MouseHover(_temp, _context, 0);
			}
			ItemSlot.Draw(spriteBatch, _temp, _context, 0, new Vector2(bounds.X, bounds.Y));

			// Empty-slot overlay (upstream `setBackground(SLOT, TURBINE_OVERLAY)`).
			if (_emptyOverlayAsset is { } asset && _temp[0].IsAir)
			{
				_emptyOverlayTex ??= ModContent.Request<Texture2D>(asset);
				if (_emptyOverlayTex?.Value is { } overlayTex)
				{
					float s = Main.inventoryScale;
					var dest = new Rectangle(
						bounds.X + (bounds.Width  - (int)(overlayTex.Width  * s)) / 2,
						bounds.Y + (bounds.Height - (int)(overlayTex.Height * s)) / 2,
						(int)(overlayTex.Width  * s),
						(int)(overlayTex.Height * s));
					spriteBatch.Draw(overlayTex, dest, Color.White);
				}
			}
		}
		finally
		{
			Main.inventoryScale = oldScale;
		}
	}

	// tML press-edge - independent of Main.mouseLeftRelease + ModalEscape.
	public override void LeftMouseDown(UIMouseEvent evt)
	{
		base.LeftMouseDown(evt);
		Dispatch(left: true);
	}

	public override void RightMouseDown(UIMouseEvent evt)
	{
		base.RightMouseDown(evt);
		Dispatch(left: false);
	}

	private void Dispatch(bool left)
	{
		if (_slotsForRender is null || _slotIndex >= _slotsForRender.Length) return;

		// Upstream BlockableSlotWidget.isBlocked.
		if (_isBlocked is { } gate && gate()) return;

		bool shiftHeld = Main.keyState.IsKeyDown(Keys.LeftShift)
		              || Main.keyState.IsKeyDown(Keys.RightShift);
		bool cursorHeld = !Main.mouseItem.IsAir;

		// Mirror of SlotWidget's canPutItems=false - output slots refuse
		// deposits from EITHER button; pickup paths (empty cursor, shift-out) work.
		bool isDeposit = cursorHeld && (left ? !shiftHeld : true);
		if (_isOutput && isDeposit) return;

		// Sound only fires when something moves. Local view is correct for the
		// common case; the rare race costs a wasted/missing chirp, never a dupe.
		bool slotEmpty   = _slotsForRender[_slotIndex].IsAir;
		bool cursorEmpty = !cursorHeld;

		if (left && shiftHeld)
		{
			if (slotEmpty) return;
			MachineActions.Send(new SlotAction(_group, _slotIndex, SlotAction.Kind.ShiftClickOut, Main.mouseItem), _entity);
			SoundEngine.PlaySound(SoundID.Grab);
			return;
		}
		if (slotEmpty && cursorEmpty) return;

		if (left)
		{
			MachineActions.Send(new SlotAction(_group, _slotIndex, SlotAction.Kind.Left, Main.mouseItem), _entity);
			SoundEngine.PlaySound(SoundID.Grab);
		}
		else
		{
			MachineActions.Send(new SlotAction(_group, _slotIndex, SlotAction.Kind.Right, Main.mouseItem), _entity);
			SoundEngine.PlaySound(SoundID.MenuTick);
		}
	}
}
