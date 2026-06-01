#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using GregTechCEuTerraria.TerrariaCompat.Tiles;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Hosts the global recipe browser UIState. Opened via TooManyItemsTile RMB,
// the inventory button, R/U hover hotkeys, or Open()/Close(). Singleton -
// re-open is a no-op except when an item filter is being applied.
public sealed class GlobalRecipeBrowserSystem : ModSystem
{
	private static GlobalRecipeBrowserSystem? _instance;
	private UserInterface? _ui;
	private GlobalRecipeBrowserState? _state;

	private const string LayerName = "GregTechCEuTerraria: GlobalRecipeBrowser";
	private const int LayerPriority = 10;

	public override void Load()
	{
		_instance = this;
		if (!Main.dedServ)
		{
			_ui = new UserInterface();
			_state = new GlobalRecipeBrowserState();
			_state.Activate();
			// High priority so other mod-side modals defer input while open.
			UILayers.RegisterModal(LayerName, () => IsOpen, LayerPriority);
		}
	}

	public override void Unload()
	{
		_instance = null;
		_ui = null;
		_state = null;
	}

	public override void OnWorldLoad()
	{
		// Pre-bake search-text cache + loot registry so the first open doesn't hitch.
		if (!Main.dedServ)
		{
			RecipeSearch.WarmCache();
			Loot.LootRegistry.Warm();
		}
	}

	public static void Open()
	{
		if (_instance is null || _instance._ui is null || _instance._state is null) return;
		if (_instance._ui.CurrentState == _instance._state) return;
		_instance._state.RebuildFromScratch();
		_instance._ui.SetState(_instance._state);
	}

	// R/U hover hotkeys - scopes to one item (Output = how-to-obtain, Input = used-as).
	public static void OpenFiltered(int itemType, GlobalRecipeBrowserState.BrowseFilter filter)
	{
		if (_instance is null || _instance._ui is null || _instance._state is null) return;
		if (_instance._ui.CurrentState != _instance._state)
			_instance._ui.SetState(_instance._state);
		_instance._state.ApplyItemFilter(itemType, filter);
	}

	public static void OpenFilteredFluid(string fluidId, string label,
		GlobalRecipeBrowserState.BrowseFilter filter)
	{
		if (_instance is null || _instance._ui is null || _instance._state is null) return;
		if (_instance._ui.CurrentState != _instance._state)
			_instance._ui.SetState(_instance._state);
		_instance._state.ApplyFluidFilter(fluidId, label, filter);
	}

	public static void OpenFilteredTag(string tagLabel, HashSet<int> items,
		GlobalRecipeBrowserState.BrowseFilter filter)
	{
		if (_instance is null || _instance._ui is null || _instance._state is null) return;
		if (_instance._ui.CurrentState != _instance._state)
			_instance._ui.SetState(_instance._state);
		_instance._state.ApplyTagFilter(tagLabel, items, filter);
	}

	public static void Close()
	{
		if (_instance?._ui is null) return;
		_instance._state?.SaveQueryForReopen();   // restore on next Open
		_instance._ui.SetState(null);
		Widgets.UISearchBar.UnfocusAll();          // release captured focus
	}

	public static bool IsOpen => _instance?._ui?.CurrentState != null;

	public override void UpdateUI(GameTime gameTime)
	{
		// Vanilla fullscreen took over - close before _ui.Update runs.
		if (IsOpen && (Main.ingameOptionsWindow || Main.gameMenu))
		{
			Close();
			return;
		}

		if (IsOpen && _state != null) ModalEscape.SuppressVanillaUIClicks(_state);

		_ui?.Update(gameTime);
	}

	// Esc handled in RecipeBrowserKeybinds.PostUpdateInput alongside R/U.
	public override void PostUpdateInput()
	{
		if (Main.dedServ || !IsOpen || _state is null) return;
		ModalEscape.SuppressItemUse(_state);
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		UILayers.InsertButton(layers,
			"GregTechCEuTerraria: TooManyItems Button",
			() => { DrawInventoryButton(); return true; });

		// High priority - browser draws on top of any other mod-side modal
		// (R/U over a machine UI opens the browser without closing it).
		UILayers.InsertModal(layers,
			LayerName,
			() =>
			{
				if (IsOpen)
				{
					_ui!.Draw(Main.spriteBatch, new GameTime());
					ModalEscape.DebugDrawAreas(Main.spriteBatch, _ui.CurrentState, "RecipeBrowser");
				}
				return true;
			},
			priority: LayerPriority);
	}

	// Fixed-anchor (580, 86) - right of ammo column (x~578), below the 48 px
	// Questbook book icon. The three mod-side inventory buttons live at y=86/122/158
	// (30 px button + 6 px gap). Fixed position avoids the bestiary chest-shift.
	private static void DrawInventoryButton()
	{
		if (!Main.playerInventory || Main.dedServ) return;

		var rect = new Rectangle(580, 86, 30, 30);

		bool hover = rect.Contains(new Point(Main.mouseX, Main.mouseY))
			&& !PlayerInput.IgnoreMouseInterface;
		if (hover)
		{
			Main.LocalPlayer.mouseInterface = true;
			if (Main.mouseLeft && Main.mouseLeftRelease)
			{
				Main.mouseLeftRelease = false;
				SoundEngine.PlaySound(SoundID.MenuTick);
				Open();
			}
		}

		var sb = Main.spriteBatch;
		var px = TextureAssets.MagicPixel.Value;

		var plate = TooManyItemsArt.PlateTexture;
		if (plate != null)
			sb.Draw(plate, rect, Color.White);
		else
			sb.Draw(px, rect, new Color(38, 42, 70));

		if (hover)
		{
			sb.Draw(px, rect, Color.White * 0.16f);
			var b = new Color(255, 235, 140);
			sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, 1), b);
			sb.Draw(px, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), b);
			sb.Draw(px, new Rectangle(rect.X, rect.Y, 1, rect.Height), b);
			sb.Draw(px, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), b);
			Main.instance.MouseText("Open Too Many Items");
		}
	}
}
