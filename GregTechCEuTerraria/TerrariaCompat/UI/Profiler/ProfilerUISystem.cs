#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using ProfilerCore = GregTechCEuTerraria.TerrariaCompat.Profiler.Profiler;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Profiler;

// Profiler modal + inventory-button host. (580, 158) - third in the stack.
public sealed class ProfilerUISystem : ModSystem
{
	private static ProfilerUISystem? _instance;
	private UserInterface? _ui;

	private const string LayerName = "GregTechCEuTerraria: Profiler";

	public override void Load()
	{
		_instance = this;
		if (!Main.dedServ)
		{
			_ui = new UserInterface();
			UILayers.RegisterModal(LayerName, () => IsOpen);
		}
	}

	public override void Unload()
	{
		_instance = null;
		_ui = null;
	}

	public static bool IsOpen => _instance?._ui?.CurrentState != null;

	public static void Open()
	{
		if (!ProfilerCore.Enabled) return;
		if (_instance?._ui is null) return;
		var state = new ProfilerUIState();
		state.Activate();
		_instance._ui.SetState(state);
	}

	public static void Close() => _instance?._ui?.SetState(null);

	public static void Toggle() { if (IsOpen) Close(); else Open(); }

	public override void PostUpdateInput()
	{
		if (Main.dedServ) return;
		if (_ui?.CurrentState is { } s) ModalEscape.SuppressItemUse(s);
		if (IsOpen && ModalEscape.EscJustPressed)
		{
			Close();
			ModalEscape.ConsumeEscape();
		}
	}

	public override void UpdateUI(GameTime gameTime)
	{
		if (IsOpen && (!Main.playerInventory || Main.ingameOptionsWindow || Main.gameMenu))
		{
			Close();
			return;
		}
		if (_ui?.CurrentState is { } s) ModalEscape.SuppressVanillaUIClicks(s);
		if (!UILayers.IsAnyHigherPriorityModalOpen(LayerName))
			_ui?.Update(gameTime);
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		UILayers.InsertButton(layers,
			"GregTechCEuTerraria: Profiler Button",
			() => { DrawInventoryButton(); return true; });

		UILayers.InsertModal(layers,
			LayerName,
			() =>
			{
				if (IsOpen) _ui!.Draw(Main.spriteBatch, new GameTime());
				return true;
			});
	}

	private static void DrawInventoryButton()
	{
		if (!ProfilerCore.Enabled) return;
		if (!Main.playerInventory || Main.dedServ) return;

		var rect = new Rectangle(580, 158, 30, 30);

		bool hover = rect.Contains(new Point(Main.mouseX, Main.mouseY))
			&& !PlayerInput.IgnoreMouseInterface;
		if (hover)
		{
			Main.LocalPlayer.mouseInterface = true;
			if (Main.mouseLeft && Main.mouseLeftRelease)
			{
				Main.mouseLeftRelease = false;
				SoundEngine.PlaySound(SoundID.MenuTick);
				Toggle();
			}
		}

		var sb = Main.spriteBatch;
		var px = TextureAssets.MagicPixel.Value;

		sb.Draw(px, rect, new Color(30, 50, 40));

		// MagicPixel "ascending bars" glyph - no asset bake required.
		int gx = rect.X + 6, gy = rect.Y + 6, gh = 18;
		var bar = new Color(140, 220, 180);
		sb.Draw(px, new Rectangle(gx,        gy + gh - 6,  4, 6),  bar);
		sb.Draw(px, new Rectangle(gx + 7,    gy + gh - 12, 4, 12), bar);
		sb.Draw(px, new Rectangle(gx + 14,   gy + gh - 18, 4, 18), bar);

		if (hover)
		{
			sb.Draw(px, rect, Color.White * 0.16f);
			var b = new Color(255, 235, 140);
			sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, 1), b);
			sb.Draw(px, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), b);
			sb.Draw(px, new Rectangle(rect.X, rect.Y, 1, rect.Height), b);
			sb.Draw(px, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), b);
			Main.instance.MouseText("Open Profiler");
		}
	}
}
