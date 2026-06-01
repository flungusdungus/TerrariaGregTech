#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Magnet analogue of MachineUISystem. No viewer packet / handshake since the
// magnet is a private inventory item; edits mutate the local ModItem and
// persist per-stack via MagnetItem.SaveData / NetSend.
public sealed class MagnetUISystem : ModSystem
{
	private UserInterface? _ui;
	private MagnetUIState? _state;

	private const string LayerName = "GregTechCEuTerraria: Magnet UI";

	public override void Load()
	{
		if (Main.dedServ) return;
		_state = new MagnetUIState();
		_ui = new UserInterface();
		UILayers.RegisterModal(LayerName, () => IsOpen);
	}

	public override void Unload()
	{
		_state = null;
		_ui = null;
	}

	public static void OpenFor(Item magnet)
	{
		var sys = ModContent.GetInstance<MagnetUISystem>();
		if (sys?._ui is null || sys._state is null) return;
		sys._state.Bind(magnet);
		sys._ui.SetState(sys._state);
		Main.playerInventory = true;
		SoundEngine.PlaySound(SoundID.MenuOpen);
	}

	public static void Close()
	{
		var sys = ModContent.GetInstance<MagnetUISystem>();
		if (sys?._ui is null || sys._state is null) return;
		if (sys._ui.CurrentState == null) return;
		sys._state.Unbind();
		sys._ui.SetState(null);
		Widgets.UITextField.UnfocusAll();
		SoundEngine.PlaySound(SoundID.MenuClose);
	}

	public static bool IsOpen
	{
		get
		{
			var sys = ModContent.GetInstance<MagnetUISystem>();
			return sys?._ui?.CurrentState != null;
		}
	}

	public override void UpdateUI(GameTime gameTime)
	{
		if (_ui is null) return;

		if (_ui.CurrentState != null)
		{
			// Auto-close on inventory-close or once the magnet leaves the inv.
			if (!Main.playerInventory || _state is null || !MagnetStillHeld(_state.Magnet))
			{
				Close();
				return;
			}
		}

		if (IsOpen && _state != null) ModalEscape.SuppressVanillaUIClicks(_state);

		if (!UILayers.IsAnyHigherPriorityModalOpen(LayerName))
			_ui.Update(gameTime);
	}

	public override void PostUpdateInput()
	{
		if (Main.dedServ || !IsOpen || _state is null) return;
		ModalEscape.SuppressItemUse(_state);
	}

	private static bool MagnetStillHeld(Item? magnet)
	{
		if (magnet is null || magnet.IsAir) return false;
		var inv = Main.LocalPlayer.inventory;
		for (int i = 0; i < inv.Length; i++)
			if (ReferenceEquals(inv[i], magnet)) return true;
		return false;
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		UILayers.InsertModal(layers,
			LayerName,
			() =>
			{
				if (_ui?.CurrentState != null)
				{
					_ui.Draw(Main.spriteBatch, new GameTime());
					ModalEscape.DebugDrawAreas(Main.spriteBatch, _ui.CurrentState, "MagnetUI");
				}
				return true;
			});
	}
}
