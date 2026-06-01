#nullable enable
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Net;
using GregTechCEuTerraria.TerrariaCompat.UI;
using GregTechCEuTerraria.TerrariaCompat.UI.Layouts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Super Tank tile - definition-driven, with a thin rendering/interaction
// subclass: it draws the stored fluid on the tank face (PostDraw) and handles
// bucket + fluid-cell right-clicks. Same pattern as TransformerTile/SolarPanelTile.
public class SuperTankTile : TieredMachineTile
{
	public SuperTankTile() { }
	public SuperTankTile(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	protected override Color MapColor    => new(120, 160, 220);
	protected override int   MineDustType => Terraria.ID.DustID.Glass;

	// Render the stored fluid on the tank face - one continuous draw covering
	// the whole machine square inset by a 4px (art) border.
	public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
	{
		base.PostDraw(i, j, spriteBatch);

		if (!MachineCellResolver.TryFindAt<SuperTankTileEntity>(i, j, out var tank)) return;

		var (w, h) = tank.Size;
		int originX = tank.Position.X, originY = tank.Position.Y;
		// Draw once, from the BOTTOM-RIGHT sub-tile. Tiles render top-left ->
		// bottom-right, so the bottom-right cell is the last of the machine's
		// cells drawn - the fluid lands on top of every base texture.
		if (i != originX + w - 1 || j != originY + h - 1) return;

		var stored = tank.GetTank(0);
		if (stored.IsEmpty || stored.Type is null) return;

		Vector2 zero = Main.drawToScreen ? Vector2.Zero
			: new Vector2(Main.offScreenRange, Main.offScreenRange);
		// Anchor on the machine's top-left tile regardless of which cell fired.
		Vector2 pos = new Vector2(originX * 16 - (int)Main.screenPosition.X,
		                          originY * 16 - (int)Main.screenPosition.Y) + zero;

		// 8 screen px = 4 of the machine's art pixels (the face art is 2x
		// upscaled, so one art pixel spans 2 screen px).
		const int Border = 8;
		var inner = new Rectangle((int)pos.X + Border, (int)pos.Y + Border,
		                          w * 16 - 2 * Border, h * 16 - 2 * Border);
		FluidIconRenderer.Draw(spriteBatch, stored.Type, inner, light: Lighting.GetColor(originX, originY));
	}

	public override bool RightClick(int i, int j)
	{
		// Multi-tile-aware: clicking any of the 2x2 sub-cells finds the entity at origin.
		if (!MachineCellResolver.TryFindAt<SuperTankTileEntity>(i, j, out var tank)) return false;

		// Always open the GUI. The previous "RMB-with-bucket to fill/drain"
		// path went through TankInteractPacket which broadcast vanilla
		// SyncEquipment to swap the bucket type server-side - but vanilla
		// treats player inventory as client-authoritative and silently drops
		// SyncEquipment packets routed back to the originating client
		// (MessageBuffer.cs:403), so the bucket never type-swapped on the
		// origin client and a later inventory sync from that client
		// overwrote the server's swapped view. Dupe (empty same bucket into
		// tank repeatedly). Fluid transfers now run through the in-GUI
		// FluidSlotWidget -> FluidSlotAction, which uses our own cursor
		// protocol (CursorUpdatePacket directly mutates Main.mouseItem on
		// the originator) and bypasses vanilla's ignore-self gate.
		MachineUISystem.OpenFor(tank, SuperTankLayout.Build(tank));
		return true;
	}
}
