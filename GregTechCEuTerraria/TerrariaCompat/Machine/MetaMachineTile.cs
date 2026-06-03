#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Cover;
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

public interface IMachineTextureSpec
{
	Rendering.MachineRenderer.Casing CasingKind { get; }
	string OverlayDir { get; }
	string OverlayBasename { get; }
	// Back->front: pipe / tinted / emissive over directional. "" = skip layer.
	string PipeOverlayBasename     { get => ""; }
	string TintedOverlayBasename   { get => ""; }
	string EmissiveOverlayBasename { get => ""; }
	// Multi-controller appearance casing (heatproof / watertight / etc.);
	// mirrors upstream workableCasingModel(appearance, overlay). null = tier default.
	string? CustomCasingTexturePath { get => null; }
	string? CustomFaceAssetPath { get => null; }
}

// Non-generic surface for MachinePlacedPacket (server only has the tile type).
public interface IMetaMachineTile
{
	int PlaceEntity(int i, int j);
}

// Abstract base for machine ModTiles. Tile exposes MachineDefinition;
// entity resolved via MachineFamilyEntity. Concrete subclasses registered
// by TieredMachineFactory per (definition x tier).
// Deliberately NOT a port of MetaMachineBlock - rotation / facing / paint
// are meaningless in 2D same-cell-wiring.
public abstract class MetaMachineTile : ModTile, IMachineTextureSpec, IMetaMachineTile, Rendering.ITextureWarmUp
{
	public abstract MachineDefinition Definition { get; }

	private MetaMachine ResolveEntity() => MachineFamilyEntity.For(Definition.Family);

	// Placing client only; MP routes entity creation via MachinePlacedPacket.
	public override void PlaceInWorld(int i, int j, Item item)
	{
		TagCompound? portable = ExtractPortable(item);

		if (Main.netMode == NetmodeID.MultiplayerClient)
		{
			var td = TileObjectData.GetTileData(Type, 0);
			int w = td?.Width ?? 1, h = td?.Height ?? 1;
			// Tile MUST reach server before entity request or
			// IsTileValidForEntity culls the freshly-created entity.
			NetMessage.SendTileSquare(Main.myPlayer, i, j, w, h);
			MachinePlacedPacket.SendRequest(i, j, Type, portable);
			return;
		}

		int id = PlaceEntity(i, j);
		if (portable is { Count: > 0 }
		    && TileEntity.ByID.TryGetValue(id, out var te) && te is MetaMachine placed)
			placed.ReadPortableData(portable);
	}

	// SP + MP server. Identity stamped here so the first save captures it.
	public int PlaceEntity(int i, int j)
	{
		// PlaceInWorld is called with the CURSOR cell (= bottom-right for
		// Origin(1,1) Style2x2). Walk TileFrame back to the top-left origin.
		Tile cell = Main.tile[i, j];
		int originX = i - (cell.TileFrameX / 18);
		int originY = j - (cell.TileFrameY / 18);

		var proto = ResolveEntity();
		int id = proto.Place(originX, originY);
		if (TileEntity.ByID.TryGetValue(id, out var te) && te is MetaMachine placed)
			placed.OverrideIdentity(Definition.Id, TileTier);

		// MP server bypasses GlobalTile.PlaceInWorld; remote-placed machines
		// would otherwise leave adjacent pipes permanently unsubscribed.
		Pipelike.PipeNeighborWatcher.NotifyAround(originX, originY);
		return id;
	}

	private static TagCompound? ExtractPortable(Item item) =>
		item.TryGetGlobalItem<MachinePortableData>(out var g) && g.Data is { Count: > 0 }
			? g.Data : null;

	// KillMultiTile fires ONCE per multi after GetItemDrops + the per-cell
	// KillTile loop. Kill by position - ModTileEntity.Kill's type compare is
	// byte-truncated and broken for Type >= 256.
	public override void KillMultiTile(int i, int j, int frameX, int frameY)
	{
		var pos = new Point16(i, j);
		if (!TileEntity.ByPosition.TryGetValue(pos, out var te) || te is not MetaMachine entity)
			return;
		entity.OnKill();
		TileEntity.ByID.Remove(entity.ID);
		TileEntity.ByPosition.Remove(pos);
	}

	// Runs before KillMultiTile - entity still alive for portable data.
	public override IEnumerable<Item> GetItemDrops(int i, int j)
	{
		if (!Mod.TryFind<ModItem>(Name, out var modItem))
			yield break;

		var drop = new Item();
		drop.SetDefaults(modItem.Type);

		if (MachineCellResolver.TryFindMachineAt(i, j, out var machine)
		    && drop.TryGetGlobalItem<MachinePortableData>(out var carrier))
		{
			var tag = new TagCompound();
			machine.WritePortableData(tag);
			if (tag.Count > 0) carrier.Data = tag;
		}
		yield return drop;
	}

	// Call from SetStaticDefaults BEFORE TileObjectData.addTile.
	protected void ApplyDefaults()
	{
		Main.tileFrameImportant[Type] = true;
		Main.tileSolid[Type]          = false;
		Main.tileSolidTop[Type]       = true;
		Main.tileBlockLight[Type]     = false;
		Main.tileLavaDeath[Type]      = false;
		Main.tileLighted[Type]        = true;

		TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
		TileObjectData.newTile.LavaDeath    = false;
		// Origin(1,1) + CenteredPlacementPlayer snaps 2x2 visual centre to cursor.
		TileObjectData.newTile.Origin       = new Point16(1, 1);
		TileObjectData.newTile.AnchorBottom = default(AnchorData);

		if (Mod.TryFind<ModItem>(Name, out var modItem))
			RegisterItemDrop(modItem.Type);

		Players.CenteredPlacementPlayer.CenteredPlacementTiles.Add(Type);
	}

	public abstract Rendering.MachineRenderer.Casing CasingKind { get; }
	public abstract string OverlayDir { get; }
	public virtual string OverlayBasename         => "overlay_front";
	public virtual string PipeOverlayBasename     => "";
	public virtual string TintedOverlayBasename   => "";
	public virtual string EmissiveOverlayBasename => "";
	public virtual string? CustomCasingTexturePath => null;
	public virtual string? CustomFaceAssetPath => null;
	protected virtual Common.Energy.VoltageTier TileTier => Common.Energy.VoltageTier.LV;

	public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
	{
		Rendering.MachineRenderer.EnsureTileTexture(Type, this, TileTier);
		return true;
	}

	// Eager install so placement ghost / minimap don't flash the placeholder.
	public virtual void WarmUpTexture() =>
		Rendering.MachineRenderer.EnsureTileTexture(Type, this, TileTier);

	public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
	{
		MachineCellResolver.TryFindMachineAt(i, j, out var machine);

		// Bound parts reskin to the controller's appearance casing (drawn
		// BEFORE the active overlay so arrows stay on top).
		if (machine is Multiblock.Part.MultiblockPartMachine part && part.IsFormed())
		{
			foreach (var ctrl in part.GetControllers())
			{
				if (ctrl.FusedCasingTileType == 0) continue;
				var sheet = Rendering.MachineRenderer.GetFusedSheet(
					Type, this, TileTier,
					ctrl.FusedCasingTileType,
					ctrl.FusedCasingTexture);
				if (sheet != null)
				{
					Rendering.MachineRenderer.DrawFusedComposite(spriteBatch, i, j, sheet);
					break;
				}
			}
		}

		if (machine != null && machine.WorkingEnabled && machine.IsActive)
			Rendering.MachineRenderer.DrawActiveOverlay(spriteBatch, i, j, OverlayDir, OverlayBasename);

		machine?.DrawCustomOverlay(spriteBatch, i, j);

		// Anchor-cell only - PostDraw per cell would 4x over-spawn.
		if (machine != null && i == machine.Position.X && j == machine.Position.Y)
			machine.OnClientFrame();

		DrawSmartInteractHighlight(i, j, spriteBatch);

		// Multi ghost preview is drawn from MultiblockPreviewSystem.PostDrawTiles
		// (in-pass draw would be occluded by later tile writes).
	}

	// Runtime-composited tiles have no per-tile HighlightMask; cells draw
	// their own perimeter edges to compose one outline.
	private static void DrawSmartInteractHighlight(int i, int j, SpriteBatch sb)
	{
		if (!Main.InSmartCursorHighlightArea(i, j, out bool selected)) return;
		if (!MachineCellResolver.TryFindMachineAt(i, j, out var machine)) return;

		int ox = machine.Position.X, oy = machine.Position.Y;
		var (w, h) = machine.Size;
		bool top    = j == oy;
		bool bottom = j == oy + h - 1;
		bool left   = i == ox;
		bool right  = i == ox + w - 1;
		if (!top && !bottom && !left && !right) return;

		Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange);
		var p = new Vector2(i * 16, j * 16) - Main.screenPosition + zero;
		int x = (int)p.X, y = (int)p.Y;
		var px = Terraria.GameContent.TextureAssets.MagicPixel.Value;
		var col = Colors.GetSelectionGlowColor(selected, 255);

		if (top)    sb.Draw(px, new Rectangle(x, y, 16, 1), col);
		if (bottom) sb.Draw(px, new Rectangle(x, y + 15, 16, 1), col);
		if (left)   sb.Draw(px, new Rectangle(x, y, 1, 16), col);
		if (right)  sb.Draw(px, new Rectangle(x + 15, y, 1, 16), col);
	}

	public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
	{
		if (!MachineCellResolver.TryFindMachineAt(i, j, out var machine)) return;
		if (!machine.WorkingEnabled || !machine.IsActive) return;
		var c = machine.WorkingLightColor;
		r += c.X;
		g += c.Y;
		b += c.Z;
	}

	// HitWire fires per cell; TryConsumeWirePulse dedups to one pulse.
	public override void HitWire(int i, int j)
	{
		if (!MachineCellResolver.TryFindMachineAt(i, j, out var machine)) return;
		if (!machine.TryConsumeWirePulse()) return;
		foreach (var cover in ((ICoverable)machine).GetCovers())
			if (cover is IWirePulseReceiver receiver)
				receiver.OnWirePulse();
	}

	// Smart cursor would otherwise redirect RMB to nearby vanilla interactables.
	// Skipped for GUI-less machines (LayoutKey == "none").
	public override bool HasSmartInteract(int i, int j,
		Terraria.GameContent.ObjectInteractions.SmartInteractScanSettings settings)
		=> Definition.LayoutKey != "none";

	public override void ModifySmartInteractCoords(
		ref int width, ref int height, ref int frameWidth, ref int frameHeight, ref int extraY)
	{
		width = 2;
		height = 2;
	}
}
