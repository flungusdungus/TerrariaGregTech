#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// 5-button plus-layout direction selector (Up/Left/Center/Right/Down). Center
// = Off (IODirection.None). Clicking the active direction toggles auto-output
// (upstream's plain-screwdriver behaviour). 54x54 logical, scaled by layout.
public sealed class UIDirectionSelector : UIElement
{
	public enum Mode { Items, Fluids }

	public const int Cell = 18;
	public const int ClusterSize = Cell * 3;

	private readonly Mode _mode;
	private readonly Func<IODirection> _getSide;
	private readonly Action<IODirection> _setSide;
	private readonly Func<bool> _getAutoOutput;
	private readonly Action<bool> _setAutoOutput;

	private bool _leftDown;
	private static Asset<Texture2D>? _btnFrame;
	private static Asset<Texture2D>? _arrow;
	private static Asset<Texture2D>? _itemCenter;
	private static Asset<Texture2D>? _fluidCenter;
	private static Asset<Texture2D>? _activeOn;
	private static Asset<Texture2D>? _activeOff;

	public UIDirectionSelector(Mode mode,
		Func<IODirection> getSide, Action<IODirection> setSide,
		Func<bool> getAutoOutput, Action<bool> setAutoOutput)
	{
		_mode = mode;
		_getSide = getSide;
		_setSide = setSide;
		_getAutoOutput = getAutoOutput;
		_setAutoOutput = setAutoOutput;
		Width = StyleDimension.FromPixels(ClusterSize);
		Height = StyleDimension.FromPixels(ClusterSize);
	}

	// Plus pattern: (1,0)=Up, (0,1)=Left, (1,1)=center/Off, (2,1)=Right, (1,2)=Down.
	// Corners return null (= not a cell).
	private static IODirection? CellFor(int col, int row)
	{
		return (col, row) switch
		{
			(1, 0) => IODirection.Up,
			(0, 1) => IODirection.Left,
			(1, 1) => IODirection.None,
			(2, 1) => IODirection.Right,
			(1, 2) => IODirection.Down,
			_      => null,
		};
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		LoadAssets();
		var bounds = GetDimensions().ToRectangle();
		float cellW = bounds.Width / 3f;
		float cellH = bounds.Height / 3f;

		var currentSide = _getSide();
		bool autoOn = _getAutoOutput();
		var mouse = new Point((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);
		IODirection? hoveredDir = null;

		TerrariaCompat.UI.PointClampDraw.Draw(spriteBatch, () =>
		{
			for (int row = 0; row < 3; row++)
			for (int col = 0; col < 3; col++)
			{
				var dir = CellFor(col, row);
				if (dir is null) continue;
				var cellRect = new Rectangle(
					bounds.X + (int)(col * cellW),
					bounds.Y + (int)(row * cellH),
					(int)cellW + 1,
					(int)cellH + 1);

				bool isActive = dir.Value == currentSide;
				bool isHover  = cellRect.Contains(mouse);
				if (isHover) hoveredDir = dir;

				DrawCell(spriteBatch, cellRect, dir.Value, isActive, isHover, autoOn);
			}
		});

		if (hoveredDir is not null)
		{
			Main.LocalPlayer.mouseInterface = true;
			string label = _mode == Mode.Items ? "Item output" : "Fluid output";
			string tip = hoveredDir.Value switch
			{
				IODirection.None  => $"{label}: OFF",
				_                 => $"{label}: {hoveredDir.Value}\n"
					+ (hoveredDir.Value == currentSide
						? $"(active - click again to {(autoOn ? "disable" : "enable")} auto-output)"
						: "(click to select)"),
			};
			Main.instance.MouseText(tip);
			HandleClick(hoveredDir.Value, currentSide, autoOn);
		}
		_leftDown = Main.mouseLeft;
	}

	private void HandleClick(IODirection clickedDir, IODirection currentSide, bool autoOn)
	{
		if (!(Main.mouseLeft && !_leftDown)) return;

		if (clickedDir == IODirection.None)
		{
			_setSide(IODirection.None);
			_setAutoOutput(false);
			Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
			return;
		}
		if (clickedDir == currentSide)
		{
			_setAutoOutput(!autoOn);   // click active -> toggle auto-output
			Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
			return;
		}
		// New direction - don't auto-enable, user clicks again to start pushing.
		_setSide(clickedDir);
		_setAutoOutput(false);
		Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
	}

	private void DrawCell(SpriteBatch sb, Rectangle dest, IODirection dir, bool isActive, bool isHover, bool autoOn)
	{
		if (_btnFrame?.Value is { } frame)
			sb.Draw(frame, dest, isHover ? Color.LightGoldenrodYellow : Color.White);

		// Centre = mode icon; arms = rotated arrow.
		if (dir == IODirection.None)
		{
			var center = _mode == Mode.Items ? _itemCenter : _fluidCenter;
			if (center?.Value is { } tex)
			{
				var srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				var dst = Shrink(dest, 2);
				sb.Draw(tex, dst, srcRect, Color.White);
			}
		}
		else if (_arrow?.Value is { } arrowTex)
		{
			// left.png points left; rotate to face each direction.
			float rot = dir switch
			{
				IODirection.Left  => 0f,
				IODirection.Up    => MathHelper.PiOver2,
				IODirection.Right => MathHelper.Pi,
				IODirection.Down  => MathHelper.Pi + MathHelper.PiOver2,
				_                 => 0f,
			};
			var origin = new Vector2(arrowTex.Width / 2f, arrowTex.Height / 2f);
			var center = new Vector2(dest.Center.X, dest.Center.Y);
			float scale = (dest.Width - 4f) / arrowTex.Width;
			sb.Draw(arrowTex, center, null, Color.White, rot, origin, scale, SpriteEffects.None, 0f);
		}

		// Top-right corner overlay on the active direction cell.
		if (isActive && dir != IODirection.None)
		{
			var overlay = autoOn ? _activeOn : _activeOff;
			if (overlay?.Value is { } tex)
			{
				var srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				int size = dest.Width / 2;
				var dst = new Rectangle(dest.Right - size - 1, dest.Y + 1, size, size);
				sb.Draw(tex, dst, srcRect, Color.White);
			}
		}
	}

	private static Rectangle Shrink(Rectangle r, int by) =>
		new(r.X + by, r.Y + by, r.Width - by * 2, r.Height - by * 2);

	private static void LoadAssets()
	{
		_btnFrame   ??= ModContent.Request<Texture2D>("GregTechCEuTerraria/Content/Textures/gui/widget/button");
		_arrow      ??= ModContent.Request<Texture2D>("GregTechCEuTerraria/Content/Textures/gui/widget/left");
		_itemCenter ??= ModContent.Request<Texture2D>("GregTechCEuTerraria/Content/Textures/gui/widget/button_item_output_overlay");
		_fluidCenter??= ModContent.Request<Texture2D>("GregTechCEuTerraria/Content/Textures/gui/widget/button_fluid_output_overlay");
		_activeOn   ??= ModContent.Request<Texture2D>("GregTechCEuTerraria/Content/Textures/gui/overlay/tool_auto_output");
		_activeOff  ??= ModContent.Request<Texture2D>("GregTechCEuTerraria/Content/Textures/gui/overlay/tool_disable_auto_output");
	}
}
