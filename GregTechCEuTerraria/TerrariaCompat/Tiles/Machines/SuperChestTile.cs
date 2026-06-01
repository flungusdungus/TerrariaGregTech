#nullable enable
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net;
using GregTechCEuTerraria.TerrariaCompat.UI;
using GregTechCEuTerraria.TerrariaCompat.UI.Layouts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Super Chest tile - definition-driven, with a thin rendering/interaction
// subclass: it draws the stored item on the chest face (PostDraw) and handles
// right-click insert / GUI open. Item-side mirror of SuperTankTile.
public class SuperChestTile : TieredMachineTile
{
	public SuperChestTile() { }
	public SuperChestTile(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	protected override Color MapColor     => new(150, 130, 90);
	protected override int   MineDustType => DustID.Stone;

	// Draw the stored item on the chest face - same single-draw-from-bottom-right
	// pattern as SuperTankTile.
	public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
	{
		base.PostDraw(i, j, spriteBatch);

		if (!MachineCellResolver.TryFindAt<SuperChestTileEntity>(i, j, out var chest)) return;

		var (w, h) = chest.Size;
		int originX = chest.Position.X, originY = chest.Position.Y;
		if (i != originX + w - 1 || j != originY + h - 1) return;

		var stored = chest.StoredItem;
		if (stored.IsAir) return;

		Main.instance.LoadItem(stored.type);
		Main.GetItemDrawFrame(stored.type, out var tex, out var srcFrame);
		if (tex is null) return;

		Vector2 zero = Main.drawToScreen ? Vector2.Zero
			: new Vector2(Main.offScreenRange, Main.offScreenRange);
		Vector2 pos = new Vector2(originX * 16 - (int)Main.screenPosition.X,
		                          originY * 16 - (int)Main.screenPosition.Y) + zero;
		const int Border = 8;
		var inner = new Rectangle((int)pos.X + Border, (int)pos.Y + Border,
		                          w * 16 - 2 * Border, h * 16 - 2 * Border);
		spriteBatch.Draw(tex, FitCentered(srcFrame, inner), srcFrame,
			Lighting.GetColor(originX, originY));
	}

	// Fit `src` into `box` preserving aspect ratio, centered.
	private static Rectangle FitCentered(Rectangle src, Rectangle box)
	{
		if (src.Width <= 0 || src.Height <= 0) return box;
		float scale = System.Math.Min((float)box.Width / src.Width, (float)box.Height / src.Height);
		int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
		return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
	}

	public override bool RightClick(int i, int j)
	{
		if (!MachineCellResolver.TryFindAt<SuperChestTileEntity>(i, j, out var chest)) return false;

		// Always open the GUI. The previous "RMB-with-held-item to insert" path
		// went through ChestInsertPacket, which broadcast vanilla SyncEquipment
		// to reduce the player's hand server-side. Vanilla treats player
		// inventory as client-authoritative - `MessageBuffer.cs:403` silently
		// drops SyncEquipment packets where `playerID == Main.myPlayer` - so the
		// originating client never saw its hand reduce, and a later vanilla
		// inventory sync from that client overwrote the server's reduced view.
		// Result: chest gained the items, hand kept them. Dupe.
		// Insertion now happens through the in-GUI shift-click path
		// (MachineShiftClickPlayer -> SlotAction.ShiftClickIn) and the
		// always-available Dump button for extraction, both of which use our
		// own server-authoritative cursor protocol (CursorUpdatePacket) that
		// bypasses vanilla's ignore-self gate.
		MachineUISystem.OpenFor(chest, SuperChestLayout.Build(chest));
		return true;
	}
}
