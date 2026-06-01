#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Shared modal-input primitives. Two rules for any mod-side modal:
//   1. Mouse clicks don't pass through to inventory / world below.
//   2. Esc closes ONLY this modal - not inventory or anything else on Escape.
//
// **Mouse-coord caveat.** Main.MouseScreen is in different spaces per phase:
//   - PostUpdateInput - RAW screen px (Main.mouseX set by PlayerInput.UpdateInput
//     before SetZoom_*); hit-tests against UI-space dims need `/ Main.UIScale`.
//   - UpdateUI - inside SetZoom_UI's block (Main.cs:17025), UI-space. No divide.
//   - DrawSelf in an InterfaceScaleType.UI layer - UI-space. No divide.
//   - Player.Update / ItemCheck - unscaled / world; convert per case.
//
// **Two-phase mouse capture** because mouseInterface and the click-release
// latches suppress different things in different frame phases:
//
//   - `SuppressItemUse` (PostUpdateInput) - sets `mouseInterface`. Vanilla's
//     frame order: 1 reset -> 2 PostUpdateInput <- here -> 3 Player.ItemCheck
//     (reads it) -> 4 UpdateUI. Setting in 4 would miss ItemCheck by one phase.
//
//   - `SuppressVanillaUIClicks` (UpdateUI) - clears mouseLeft/RightRelease so
//     HUD widgets (info-accessory toggles, sign hover) don't fire during
//     DrawInterface (which runs after UpdateUI).
//     **CRITICAL: never clear these in PostUpdateInput.** Player.ItemCheck reads
//     `!mouseLeftRelease` at Player.cs:24073 - clearing pre-ItemCheck swallows
//     every LMB inside the modal.
//
//   - `EscJustPressed` - stateless edge-detect on raw KeyboardState.
//   - `ConsumeEscape` - clears every trigger bound to Escape in both Keyboard
//     and KeyboardUI profiles, so vanilla's Inventory toggle reads false when
//     TriggersSet.CopyInto runs later this frame.
//
// Call pattern from a modal's ModSystem hooks:
//
//     public override void PostUpdateInput()
//     {
//         if (Main.dedServ || _state is null) return;
//         ModalEscape.SuppressItemUse(_state);
//         if (ModalEscape.EscJustPressed) { MyModal.Close(); ModalEscape.ConsumeEscape(); }
//     }
//     public override void UpdateUI(GameTime _)
//     {
//         if (_state != null) ModalEscape.SuppressVanillaUIClicks(_state);
//     }
//
// Each handler is independent - two simultaneous modals each need an Esc press.
public static class ModalEscape
{
	private static readonly InputMode[] KeyboardModes = { InputMode.Keyboard, InputMode.KeyboardUI };
	private static readonly string[] EscapeKey = { "Escape" };

	// Phase 2 - cursor-scoped. Three flags:
	//   mouseInterface=true  -> blocks Player.ItemCheck this frame.
	//   controlUseItem=false -> clears stale LMB-use input (PlayerInput set it
	//                         pre-PostUpdateInput; ItemCheck would still see true).
	//   controlUseTile=false -> LookForTileInteractions reads this directly with
	//                         no mouseInterface check (Player.cs:31456), so a
	//                         door/chest/NPC/sign under the modal still fires RMB.
	public static void SuppressItemUse(UIState state)
	{
		if (state is null) return;
		DbgSuppressItemUseHit = null;
		// PostUpdateInput is pre-SetZoom_UI so Main.MouseScreen is raw screen px,
		// but UIElement._dimensions are UI-space (parent = screen / UIScale).
		var mouse = Main.MouseScreen / Main.UIScale;
		DbgMouseAtSuppress = mouse;
		int i = 0;
		foreach (var child in state.Children)
		{
			if (child.ContainsPoint(mouse))
			{
				var p = Main.LocalPlayer;
				p.mouseInterface = true;
				p.controlUseItem = false;
				p.controlUseTile = false;
				var d = child.GetDimensions();
				DbgSuppressItemUseHit = $"#{i}:{child.GetType().Name} ({(int)d.X},{(int)d.Y} {(int)d.Width}x{(int)d.Height})";
				return;
			}
			i++;
		}
	}

	// Phase 4 - swallows vanilla click signals before DrawInterface reads them.
	// Three knobs (LMB and RMB inventory paths use different gates):
	//   mouseLeftRelease=false  -> ItemSlot.LeftClick (ItemSlot.cs:824) +
	//                             HUD widgets that gate the same way.
	//   mouseRightRelease=false -> ItemSlot.RightClick minor branches (lines
	//                             1536/1546/1561).
	//   Main.stackSplit=9999    -> ItemSlot.RightClick's MAIN stack-split branch
	//                             (line 1570) gates on `stackSplit > 1`, not on
	//                             the release latch. Vanilla decrements + resets
	//                             on release (Main.cs:61947) - auto-cleans up.
	// **Cursor-scoped** - clearing globally also kills the player's own clicks.
	public static void SuppressVanillaUIClicks(UIState state)
	{
		if (state is null) return;
		// UpdateUI runs inside SetZoom_UI's block (Main.cs:17025) so MouseScreen
		// is already UI-space here. No `/ UIScale`.
		var mouse = Main.MouseScreen;
		foreach (var child in state.Children)
		{
			if (child.ContainsPoint(mouse))
			{
				Main.mouseLeftRelease = false;
				Main.mouseRightRelease = false;
				Main.stackSplit = 9999;
				return;
			}
		}
	}

	// Edge-detect: true on the frame Esc was just pressed.
	public static bool EscJustPressed =>
		Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape);

	// Clear every Esc-bound trigger so vanilla / mod actions don't fire this
	// frame. Call before TriggersSet.CopyInto (i.e. from PostUpdateInput).
	public static void ConsumeEscape()
	{
		foreach (var mode in KeyboardModes)
			ConsumePhysicalKeys(EscapeKey, mode);
	}

	// Debug overlay - set DebugDrawHitTest=true to see modal hit-test rects +
	// a per-phase mouseInterface trace (which phase first set TRUE).
	public static bool DebugDrawHitTest = false;

	internal static bool DbgMI_AfterPostUpdateInput;
	internal static bool DbgMI_AfterUpdateUI;
	internal static bool DbgMI_AtModalDraw;
	internal static string? DbgSuppressItemUseHit;
	internal static Vector2 DbgMouseAtSuppress;

	public static void DebugDrawAreas(SpriteBatch sb, UIState? state, string label)
	{
		if (!DebugDrawHitTest || state is null) return;
		var px = TextureAssets.MagicPixel.Value;
		var mouse = Main.MouseScreen;

		// Walk the WHOLE tree - a nested widget whose bounds extend past its
		// parent's still triggers IsMouseHovering / mouseInterface setters even
		// when the top-level parent's ContainsPoint reports no match.
		void Walk(UIElement el, int depth, string path)
		{
			var d = el.GetDimensions();
			var rect = new Rectangle((int)d.X, (int)d.Y, (int)d.Width, (int)d.Height);
			bool hit = el.ContainsPoint(mouse);

			// Only draw cursor-overlapping rects so the overlay stays readable.
			if (hit)
			{
				var fill = new Color(0, 255, 0) * (0.06f + depth * 0.04f);
				sb.Draw(px, rect, fill);
				var edge = Color.LimeGreen;
				sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, 1), edge);
				sb.Draw(px, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), edge);
				sb.Draw(px, new Rectangle(rect.X, rect.Y, 1, rect.Height), edge);
				sb.Draw(px, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), edge);

				ReLogic.Graphics.DynamicSpriteFontExtensionMethods.DrawString(
					sb, FontAssets.MouseText.Value,
					$"{path}: {el.GetType().Name} {rect.Width}x{rect.Height}",
					new Vector2(rect.X + 3, rect.Y + 3 + depth * 14),
					edge);
			}

			int i = 0;
			foreach (var child in el.Children)
			{
				Walk(child, depth + 1, $"{path}.{i}");
				i++;
			}
		}

		int topIdx = 0;
		foreach (var child in state.Children)
		{
			Walk(child, 0, $"{label}#{topIdx}");
			topIdx++;
		}

		// Cursor-following readout of every "can I interact?" input flag.
		// BLOCKED outside a gizmo rect = block is elsewhere; chase that flag.
		var p = Main.LocalPlayer;
		DbgMI_AtModalDraw = p.mouseInterface;
		var lines = new[]
		{
			($"cursor=({(int)mouse.X},{(int)mouse.Y}) draw-time   |   ({(int)DbgMouseAtSuppress.X},{(int)DbgMouseAtSuppress.Y}) at PostUpdateInput  (UIScale={Main.UIScale:F2})", Color.White),
			($"PHASE TRACE mouseInterface:", Color.White),
			($"  post-SuppressItemUse  = {(DbgMI_AfterPostUpdateInput ? "TRUE  <- SuppressItemUse fired or earlier setter" : "false")}", DbgMI_AfterPostUpdateInput ? Color.Yellow : Color.Cyan),
			($"    hit child: {DbgSuppressItemUseHit ?? "(none)"}", DbgSuppressItemUseHit != null ? Color.Magenta : Color.DarkGray),
			($"  post-UpdateUI         = {(DbgMI_AfterUpdateUI ? "TRUE  <- widget.Update setter fired" : "false")}", DbgMI_AfterUpdateUI ? Color.Yellow : Color.Cyan),
			($"  at-modal-draw         = {(DbgMI_AtModalDraw ? "TRUE  <- earlier draw layer set it" : "false")}", DbgMI_AtModalDraw ? Color.Yellow : Color.Cyan),
			($"  lastMouseInterface    = {(p.lastMouseInterface ? "TRUE  (set this frame, captured pre-reset)" : "false")}", p.lastMouseInterface ? Color.Yellow : Color.Cyan),
			($"controlUseItem={(p.controlUseItem ? "ok" : "BLOCKED")}  controlUseTile={(p.controlUseTile ? "ok" : "BLOCKED")}", (p.controlUseItem && p.controlUseTile) ? Color.Cyan : Color.Yellow),
			($"blockMouse={(Main.blockMouse ? "BLOCKED" : "ok")}  buildMode={(Terraria.GameInput.PlayerInput.InBuildingMode ? "BLOCKED" : "ok")}  stackSplit={Main.stackSplit}", Color.Cyan),
		};
		var basePos = new Vector2(mouse.X + 18, mouse.Y + 18);
		for (int i = 0; i < lines.Length; i++)
		{
			ReLogic.Graphics.DynamicSpriteFontExtensionMethods.DrawString(
				sb, FontAssets.MouseText.Value, lines[i].Item1,
				basePos + new Vector2(0, i * 14), lines[i].Item2);
		}
	}

	// Generic physical-key consumer - also used by R/U hover hotkeys.
	public static void ConsumePhysicalKeys(ICollection<string> physicalKeys, InputMode mode)
	{
		if (physicalKeys.Count == 0) return;
		var profile = PlayerInput.CurrentProfile;
		if (profile is null || !profile.InputModes.TryGetValue(mode, out var cfg)) return;

		var current = PlayerInput.Triggers.Current;
		var justPressed = PlayerInput.Triggers.JustPressed;

		foreach (var binding in cfg.KeyStatus)
		{
			bool sharesKey = false;
			foreach (var key in binding.Value)
				if (physicalKeys.Contains(key)) { sharesKey = true; break; }
			if (!sharesKey) continue;

			if (current.KeyStatus.ContainsKey(binding.Key))
				current.KeyStatus[binding.Key] = false;
			if (justPressed.KeyStatus.ContainsKey(binding.Key))
				justPressed.KeyStatus[binding.Key] = false;
		}
	}
}
