#nullable enable
using GregTechCEuTerraria.Api.Capability;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.PipeSettings;

// Host for the pipe per-side settings panel. Lifecycle mirrors MachineUISystem
// (Esc/inventory close, IsInTileInteractionRange auto-close, ModalEscape during
// PostUpdateInput). Open trigger = non-layer-item RMB on a pipe cell with at
// least one cardinal capability-holder neighbour.
public sealed class PipeSettingsSystem : ModSystem
{
	private UserInterface? _ui;
	private PipeSettingsState? _state;

	private const string LayerName = "GregTechCEuTerraria: Pipe Settings";

	public override void Load()
	{
		if (Main.dedServ) return;
		_state = new PipeSettingsState();
		_state.Activate();
		_ui = new UserInterface();
		UILayers.RegisterModal(LayerName, () => IsOpen);
	}

	public override void Unload()
	{
		_state = null;
		_ui = null;
	}

	public static bool IsOpen
	{
		get
		{
			var sys = ModContent.GetInstance<PipeSettingsSystem>();
			return sys?._ui?.CurrentState != null;
		}
	}

	public static void OpenFor(int x, int y, PipeKind layer)
	{
		var sys = ModContent.GetInstance<PipeSettingsSystem>();
		if (sys?._ui is null || sys._state is null) return;
		ModUIRegistry.OnOpen(Close); // close any other mod-side modal first
		// One state handles regular + simple pipes (BuildSideCell branches).
		sys._state.Bind(x, y, layer);
		sys._ui.SetState(sys._state);
		// Open inventory so Esc -> inventory close -> us close (MachineUISystem pattern).
		Main.playerInventory = true;
		SoundEngine.PlaySound(SoundID.MenuOpen);
	}

	public static void Close()
	{
		var sys = ModContent.GetInstance<PipeSettingsSystem>();
		if (sys?._ui is null || sys._state is null) return;
		if (sys._ui.CurrentState == null) return;
		sys._state.Unbind();
		sys._ui.SetState(null);
		ModUIRegistry.OnClose(Close);
		SoundEngine.PlaySound(SoundID.MenuClose);
	}

	// "Cursor over an openable pipe" probe - PostUpdateInput writes, UpdateUI
	// reads for the tooltip. Once per frame keeps the late MouseText call
	// safe from intermediate clobbers.
	private static int _hoverX, _hoverY;
	private static PipeKind? _hoverLayer;
	private static bool _hoverHasOpenable;

	// Layer-owning items (wires / pipes / wire cutter) have their own RMB
	// behaviour and must take priority over the panel-open trigger.
	private static bool IsLayerAffectingItem(Item item)
	{
		if (item.IsAir) return false;
		if (item.ModItem is Items.Cables.WireItem) return true;
		if (item.ModItem is Items.Pipes.PipeItem)  return true;
		if (item.ModItem is Items.Pipes.SimpleItemPipeItem)  return true;
		if (item.ModItem is Items.Pipes.SimpleFluidPipeItem) return true;
		if (item.ModItem is Items.Pipes.LaserPipeItem)       return true;
		if (item.ModItem is Items.Tools.ToolItem tool && tool.IsWireCutter) return true;
		return false;
	}

	// Runs whether or not a panel is already open, so RMB on another openable
	// pipe swaps the panel (vanilla chest behaviour).
	public override void PostUpdateInput()
	{
		_hoverLayer = null;
		if (Main.dedServ) return;
		if (IsOpen && _state is not null) ModalEscape.SuppressItemUse(_state);

		var p = Main.LocalPlayer;
		if (p is null) return;
		// Layer-affecting items handle their own RMB; suppress vanilla's
		// LookForTileInteractions so a chest near the wire run doesn't open.
		if (IsLayerAffectingItem(p.HeldItem))
		{
			if (Main.mouseRight)
			{
				p.controlUseTile  = false;
				p.releaseUseTile  = false;
			}
			return;
		}

		// Two-stage cursor: raw screen->world cell first (bypasses smart-cursor
		// retarget, which steers AWAY from pipes), then Player.tileTargetX/Y
		// as fallback for non-pipe cursors.
		//
		// Inverse of vanilla PlayerInput.SetZoom_MouseInWorld - the world view
		// zoom pivots around the SCREEN CENTRE, not the origin:
		//   world = screenPosition + half + (rawMouse - half) / renderZoom.
		// Use ZoomMatrix.M11/M22 = the 1/128-snapped RenderZoom (SpriteViewMatrix
		// .RenderZoom isn't exposed by tML's assembly).
		var zoomMatrix = Main.GameViewMatrix.ZoomMatrix;
		float zoomX = zoomMatrix.M11;
		float zoomY = zoomMatrix.M22;
		if (zoomX <= 0f) zoomX = 1f;
		if (zoomY <= 0f) zoomY = 1f;
		float halfW = Main.screenWidth * 0.5f;
		float halfH = Main.screenHeight * 0.5f;
		float cursorWorldX = Main.screenPosition.X + halfW + (Main.mouseX - halfW) / zoomX;
		float cursorWorldY = Main.screenPosition.Y + halfH + (Main.mouseY - halfH) / zoomY;
		int rawX = (int)System.Math.Floor(cursorWorldX / 16f);
		int rawY = (int)System.Math.Floor(cursorWorldY / 16f);

		int x, y;
		bool TryPipeAt(int tx, int ty, out PipeKind k)
		{
			if (tx <= 0 || tx >= Main.maxTilesX - 1 || ty <= 0 || ty >= Main.maxTilesY - 1) { k = default; return false; }
			if (ItemPipeLayerSystem .Pipes.Has(tx, ty)) { k = PipeKind.Item;  return true; }
			if (FluidPipeLayerSystem.Pipes.Has(tx, ty)) { k = PipeKind.Fluid; return true; }
			if (Pipelike.Laser.LaserPipeLayerSystem.Pipes.Has(tx, ty)) { k = PipeKind.Laser; return true; }
			if (Pipelike.Optical.OpticalPipeLayerSystem.Pipes.Has(tx, ty)) { k = PipeKind.Optical; return true; }
			k = default; return false;
		}
		PipeKind? layer;
		if (TryPipeAt(rawX, rawY, out var k1)) { x = rawX; y = rawY; layer = k1; }
		else if (TryPipeAt(Player.tileTargetX, Player.tileTargetY, out var k2))
		{
			x = Player.tileTargetX; y = Player.tileTargetY; layer = k2;
		}
		else return;

		// Same vanilla tile-interaction reach the auto-close uses.
		if (!p.IsInTileInteractionRange(x, y, TileReachCheckSettings.Simple)) return;

		// Laser + optical pipes have no settings panel; force-false suppresses
		// click-to-open AND avoids the unsupported-PipeKind branch in the probe.
		bool hasLiveNeighbour = layer.Value != PipeKind.Laser && layer.Value != PipeKind.Optical
			&& PipeNeighborProbe.HasAnyLive(x, y, layer.Value);

		// Record hover for EVERY pipe (mid-run pipes get the debug breakdown,
		// just without the RMB hint). Skipped on the already-bound pipe.
		bool sameAsBound = IsOpen && _state is not null
			&& _state.PipeX == x && _state.PipeY == y && _state.Layer == layer.Value;
		if (!sameAsBound) { _hoverX = x; _hoverY = y; _hoverLayer = layer; _hoverHasOpenable = hasLiveNeighbour; }

		// Press-edge open + swap. Swallow RMB + clear BOTH controlUseTile and
		// releaseUseTile (the smart-cursor branch in LookForTileInteractions
		// checks releaseUseTile separately on press-edge).
		if (hasLiveNeighbour && Main.mouseRight && Main.mouseRightRelease && !sameAsBound)
		{
			OpenFor(x, y, layer.Value);
			Main.mouseRightRelease = false;
			p.controlUseTile = false;
			p.releaseUseTile = false;
		}
		else if (hasLiveNeighbour && Main.mouseRight && !sameAsBound)
		{
			// Held-RMB suppression - the press-edge swallow above covers only
			// the single mouseRightRelease frame; otherwise a next-frame
			// releaseUseTile re-fire leaks through.
			p.controlUseTile = false;
			p.releaseUseTile = false;
		}
	}

	public override void UpdateUI(GameTime gameTime)
	{
		if (_ui is null) return;

		if (IsOpen && _state != null) ModalEscape.SuppressVanillaUIClicks(_state);

		if (_ui.CurrentState != null)
		{
			if (!Main.playerInventory)   // Esc-via-inventory -> close
			{
				Close();
				return;
			}

			if (_state is not null)
			{
				bool stillThere = _state.Layer == PipeKind.Fluid
					? FluidPipeLayerSystem.Pipes.Has(_state.PipeX, _state.PipeY)
					: ItemPipeLayerSystem .Pipes.Has(_state.PipeX, _state.PipeY);
				if (!stillThere)
				{
					Close();
					return;
				}

				if (!Main.LocalPlayer.IsInTileInteractionRange(_state.PipeX, _state.PipeY, TileReachCheckSettings.Simple))
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

		// Route via WorldHoverTooltip's central "cursor over UI" gate - tooltip
		// AND the openable-pipe highlight both suppress under a UI panel there.
		if (_hoverLayer is not null)
		{
			WorldHoverTooltip.Set(BuildHoverTooltip(_hoverLayer.Value, _hoverX, _hoverY));
			if (_hoverHasOpenable)
				WorldHoverTooltip.SetHighlight(_hoverX, _hoverY, new Color(255, 220, 60, 200));
		}
	}

	// Per-layer contents/throughput + stable net id + RMB hint. Per-channel
	// breakdown surfaces cross-network leaks (water pipe carrying steam in
	// channel 1); net id reveals merged networks.
	private static string BuildHoverTooltip(PipeKind layer, int x, int y)
	{
		string contents = layer switch
		{
			PipeKind.Fluid   => FluidContentsLine(x, y),
			PipeKind.Laser   => LaserStatusLine(x, y),
			PipeKind.Optical => OpticalStatusLine(x, y),
			_                => ItemThroughputLine(x, y),
		};
		string netLine = NetIdLine(layer, x, y);
		var sb = new System.Text.StringBuilder();
		if (!string.IsNullOrEmpty(contents)) { sb.Append(contents); sb.Append('\n'); }
		else if (layer == PipeKind.Fluid)    { sb.Append("Pipe: empty\n"); }
		if (!string.IsNullOrEmpty(netLine))  { sb.Append(netLine);  sb.Append('\n'); }
		if (_hoverHasOpenable) sb.Append("RMB to setup the pipe");
		else                   { if (sb.Length > 0 && sb[sb.Length - 1] == '\n') sb.Length--; }
		return sb.ToString();
	}

	// "Active/Idle" + endpoint-reached hint (disconnected when neither axis
	// resolves to an ILaserContainer).
	private static string LaserStatusLine(int x, int y)
	{
		bool active = Pipelike.Laser.LaserPipeLayerSystem.IsActive(x, y);
		var net = Pipelike.Laser.LaserPipeNetSystem.Level.GetNetFromPos((x, y));
		bool hasEndpoint = false;
		if (net is not null)
		{
			foreach (var side in new[]
				{ IODirection.Up, IODirection.Down,
				  IODirection.Left, IODirection.Right })
			{
				if (net.GetNetData((x, y), side) is not null) { hasEndpoint = true; break; }
			}
		}
		string state = active ? "[c/55FF55:Active]" : "[c/AAAAAA:Idle]";
		string ep    = hasEndpoint ? "endpoint reached" : "[c/FFAA44:no endpoint]";
		return $"Laser Pipe: {state} * {ep}";
	}

	// Mirror of LaserStatusLine for optical pipes (data/computation endpoints).
	private static string OpticalStatusLine(int x, int y)
	{
		bool active = Pipelike.Optical.OpticalPipeLayerSystem.IsActive(x, y);
		var net = Pipelike.Optical.OpticalPipeNetSystem.Level.GetNetFromPos((x, y));
		bool hasEndpoint = false;
		if (net is not null)
		{
			foreach (var side in new[]
				{ IODirection.Up, IODirection.Down,
				  IODirection.Left, IODirection.Right })
			{
				if (net.GetNetData((x, y), side) is not null) { hasEndpoint = true; break; }
			}
		}
		string state = active ? "[c/55FF55:Active]" : "[c/AAAAAA:Idle]";
		string ep    = hasEndpoint ? "endpoint reached" : "[c/FFAA44:no endpoint]";
		return $"Optical Pipe: {state} * {ep}";
	}

	private static string ItemThroughputLine(int x, int y)
	{
		int v = Main.netMode == NetmodeID.MultiplayerClient
			? (ItemPipeNetSystem.ClientTransferStats.TryGetValue((x, y), out int cv) ? cv : 0)
			: (ItemPipeLayerSystem.GetSides(x, y)?.TransferredItems ?? 0);
		return v > 0 ? $"Last 1s: {v} items" : "";
	}

	private static string FluidContentsLine(int x, int y)
	{
		global::GregTechCEuTerraria.Api.Fluids.FluidStack[]? fluids;
		if (Main.netMode == NetmodeID.MultiplayerClient)
		{
			FluidPipeLayerSystem.ClientTankSnapshots.TryGetValue((x, y), out fluids);
		}
		else
		{
			fluids = FluidPipeLayerSystem.GetState(x, y)?.GetContainedFluids();
		}
		if (fluids is null) return "";
		var sb = new System.Text.StringBuilder();
		int channels = fluids.Length;
		for (int i = 0; i < channels; i++)
		{
			var f = fluids[i];
			if (f.IsEmpty) continue;
			string name = f.Type?.DisplayName ?? f.Type?.Id ?? "?";
			if (sb.Length > 0) sb.Append('\n');
			if (channels > 1) sb.Append($"ch{i}: ");   // surface per-channel asymmetry
			else              sb.Append("Pipe: ");
			sb.Append(f.Amount); sb.Append(" mB "); sb.Append(name);
		}
		return sb.ToString();
	}

	// Hash of the lex-smallest node coord - stable per network. Reads the
	// in-process net graph (host-and-play hits live server state; true
	// dedicated client has an empty static and falls through to "").
	private static string NetIdLine(PipeKind layer, int x, int y)
	{
		(int x, int y)? anchor = null;
		int nodeCount = 0;
		if (layer == PipeKind.Fluid)
		{
			var net = FluidPipeNetSystem.Level.GetNetFromPos((x, y));
			if (net is null) return "";
			nodeCount = net.AllNodes.Count;
			foreach (var k in net.AllNodes.Keys)
				if (anchor is null || k.x < anchor.Value.x || (k.x == anchor.Value.x && k.y < anchor.Value.y))
					anchor = k;
		}
		else if (layer == PipeKind.Laser)
		{
			var net = Pipelike.Laser.LaserPipeNetSystem.Level.GetNetFromPos((x, y));
			if (net is null) return "";
			nodeCount = net.AllNodes.Count;
			foreach (var k in net.AllNodes.Keys)
				if (anchor is null || k.x < anchor.Value.x || (k.x == anchor.Value.x && k.y < anchor.Value.y))
					anchor = k;
		}
		else if (layer == PipeKind.Optical)
		{
			var net = Pipelike.Optical.OpticalPipeNetSystem.Level.GetNetFromPos((x, y));
			if (net is null) return "";
			nodeCount = net.AllNodes.Count;
			foreach (var k in net.AllNodes.Keys)
				if (anchor is null || k.x < anchor.Value.x || (k.x == anchor.Value.x && k.y < anchor.Value.y))
					anchor = k;
		}
		else
		{
			var net = ItemPipeNetSystem.Level.GetNetFromPos((x, y));
			if (net is null) return "";
			nodeCount = net.AllNodes.Count;
			foreach (var k in net.AllNodes.Keys)
				if (anchor is null || k.x < anchor.Value.x || (k.x == anchor.Value.x && k.y < anchor.Value.y))
					anchor = k;
		}
		if (anchor is null) return "";
		uint h = 2166136261u;
		unchecked { h ^= (uint)anchor.Value.x; h *= 16777619u; h ^= (uint)anchor.Value.y; h *= 16777619u; }
		return $"Net #{(h & 0xFFFF):X4} ({nodeCount} pipes)";
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		UILayers.InsertModal(layers,
			LayerName,
			() =>
			{
				if (_ui?.CurrentState != null) _ui.Draw(Main.spriteBatch, new GameTime());
				return true;
			});

	}
}
