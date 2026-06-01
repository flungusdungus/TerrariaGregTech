#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Single open-machine-UI policy (vanilla chest behaviour). Tied to
// Main.playerInventory so the inventory panel shows alongside, giving slot
// widgets a natural drag source.
public sealed class MachineUISystem : ModSystem
{
	private UserInterface? _ui;
	private MachineUIState? _state;

	private const string LayerName = "GregTechCEuTerraria: Machine UI";

	public override void Load()
	{
		if (Main.dedServ) return;
		_state = new MachineUIState();
		_ui = new UserInterface();
		UILayers.RegisterModal(LayerName, () => IsOpen);
	}

	public override void Unload()
	{
		_state = null;
		_ui = null;
	}

	public static void OpenFor(MetaMachine entity, MachineUILayout layout)
	{
		var sys = ModContent.GetInstance<MachineUISystem>();
		if (sys?._ui is null || sys._state is null) return;
		ModUIRegistry.OnOpen(Close); // close any other mod-side modal first
		sys._state.Bind(entity, layout);
		sys._ui.SetState(sys._state);
		Main.playerInventory = true;
		SoundEngine.PlaySound(SoundID.MenuOpen);
		// Tell the server to start streaming state + accept our actions. SP
		// already has authoritative state in-process - gate to skip the alloc.
		if (Main.netMode == NetmodeID.MultiplayerClient)
			TerrariaCompat.Net.MachineViewPacket.SendBegin(entity.Position);
	}

	public static void Close()
	{
		var sys = ModContent.GetInstance<MachineUISystem>();
		if (sys?._ui is null || sys._state is null) return;
		if (sys._ui.CurrentState == null) return;   // already closed
		// Capture entity BEFORE Unbind so the End packet targets the right tile.
		var entity = sys._state.Entity;
		sys._state.Unbind();
		sys._ui.SetState(null);
		ModUIRegistry.OnClose(Close);
		Widgets.UISearchBar.UnfocusAll();
		Widgets.UITextField.UnfocusAll();
		SoundEngine.PlaySound(SoundID.MenuClose);
		if (Main.netMode == NetmodeID.MultiplayerClient && entity != null)
			TerrariaCompat.Net.MachineViewPacket.SendEnd(entity.Position);
	}

	public static bool IsOpen
	{
		get
		{
			var sys = ModContent.GetInstance<MachineUISystem>();
			return sys?._ui?.CurrentState != null;
		}
	}

	// Used by ModPlayer.ShiftClickSlot to route shift-clicks into the open
	// machine's input slots (vanilla chest parity).
	public static MetaMachine? CurrentEntity
	{
		get
		{
			var sys = ModContent.GetInstance<MachineUISystem>();
			return sys?._state?.Entity;
		}
	}

	public override void UpdateUI(GameTime gameTime)
	{
		if (_ui is null) return;

		if (IsOpen && _state != null) ModalEscape.SuppressVanillaUIClicks(_state);

		if (_ui.CurrentState != null)
		{
			if (!Main.playerInventory) { Close(); return; }   // mirror chest auto-close

			var bound = _state?.Entity;
			if (bound != null)
			{
				if (!TileEntity.ByID.ContainsKey(bound.ID))
				{
					Close();
					return;
				}

				// Same tile-interaction reach the chest UI uses (no separate
				// hardcoded distance - reach extensions extend the window).
				bool inReach = false;
				foreach (var (cx, cy) in bound.Cells())
				{
					if (Main.LocalPlayer.IsInTileInteractionRange(cx, cy, TileReachCheckSettings.Simple))
					{
						inReach = true;
						break;
					}
				}
				if (!inReach)
				{
					Close();
					return;
				}
			}
		}

		// Skip widget updates when a higher-priority modal is on top (close
		// checks above still run).
		if (!UILayers.IsAnyHigherPriorityModalOpen(LayerName))
			_ui.Update(gameTime);
		ModalEscape.DbgMI_AfterUpdateUI = Main.LocalPlayer.mouseInterface;
	}

	// PostUpdateInput per ModalEscape phase-2 rule. Esc closes via the inventory
	// (Esc -> inventory closes -> us); no per-modal Esc handler needed.
	public override void PostUpdateInput()
	{
		if (Main.dedServ || !IsOpen || _state is null) return;
		ModalEscape.SuppressItemUse(_state);
		ModalEscape.DbgMI_AfterPostUpdateInput = Main.LocalPlayer.mouseInterface;
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
					ModalEscape.DebugDrawAreas(Main.spriteBatch, _ui.CurrentState, "MachineUI");
				}
				return true;
			});
	}
}
