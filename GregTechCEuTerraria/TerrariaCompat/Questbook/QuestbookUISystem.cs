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

namespace GregTechCEuTerraria.TerrariaCompat.Questbook;

// Hosts the questbook UI + inventory button (mirrors
// GlobalRecipeBrowserSystem.DrawInventoryButton).
public sealed class QuestbookUISystem : ModSystem
{
	private static QuestbookUISystem? _instance;
	private UserInterface? _ui;

	private const string LayerName = "GregTechCEuTerraria: Questbook";

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
		if (_instance?._ui is null)
			return;
		// Fresh state so layout picks up current screen size.
		var state = new QuestbookUIState();
		state.Activate();
		_instance._ui.SetState(state);
	}

	public static void Close() => _instance?._ui?.SetState(null);

	public static void Toggle()
	{
		if (IsOpen)
			Close();
		else
			Open();
	}

	// Phase 2: suppress item use + handle Esc - see ModalEscape.
	public override void PostUpdateInput()
	{
		if (Main.dedServ) return;
		if (_ui?.CurrentState is { } s)
			ModalEscape.SuppressItemUse(s);
		if (IsOpen && ModalEscape.EscJustPressed)
		{
			Close();
			ModalEscape.ConsumeEscape();
		}
	}

	public override void UpdateUI(GameTime gameTime)
	{
		// Close with inventory / on fullscreen menu open. (Esc is consumed by
		// PostUpdateInput first.)
		if (IsOpen && (!Main.playerInventory || Main.ingameOptionsWindow || Main.gameMenu))
		{
			Close();
			return;
		}

		// Phase 4: scoped click-release suppression.
		if (_ui?.CurrentState is { } s) ModalEscape.SuppressVanillaUIClicks(s);

		// Skip update when a higher-priority modal sits on top.
		if (!UILayers.IsAnyHigherPriorityModalOpen(LayerName))
			_ui?.Update(gameTime);
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		// Z-ordering via UILayers - button above accessory bar; modal below
		// "Mouse Text" so tooltips/cursor draw over it.
		UILayers.InsertButton(layers,
			"GregTechCEuTerraria: Questbook Button",
			() => { DrawInventoryButton(); return true; });

		UILayers.InsertModal(layers,
			LayerName,
			() =>
			{
				if (IsOpen)
				{
					_ui!.Draw(Main.spriteBatch, new GameTime());
					UI.ModalEscape.DebugDrawAreas(Main.spriteBatch, _ui.CurrentState, "Questbook");
				}
				return true;
			});
	}

	// Below the recipe browser button (see GlobalRecipeBrowserSystem for
	// rationale). Fixed position - doesn't shift on chest/shop/sign UI.
	private static void DrawInventoryButton()
	{
		if (!Main.playerInventory || Main.dedServ)
			return;

		var rect = new Rectangle(580, 122, 30, 30);

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

		sb.Draw(px, rect, new Color(38, 42, 70));

		// Vanilla Book sprite, centred.
		Main.instance.LoadItem(ItemID.Book);
		Texture2D book = TextureAssets.Item[ItemID.Book].Value;
		float scale = 20f / System.Math.Max(book.Width, book.Height);
		sb.Draw(book, rect.Center.ToVector2(), null, Color.White, 0f,
			book.Size() * 0.5f, scale, SpriteEffects.None, 0f);

		if (hover)
		{
			sb.Draw(px, rect, Color.White * 0.16f);
			var b = new Color(255, 235, 140);
			sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, 1), b);
			sb.Draw(px, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), b);
			sb.Draw(px, new Rectangle(rect.X, rect.Y, 1, rect.Height), b);
			sb.Draw(px, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), b);
			Main.instance.MouseText("GregTech Quests");
		}
	}
}
