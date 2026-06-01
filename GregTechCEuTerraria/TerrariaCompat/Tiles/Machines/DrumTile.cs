#nullable enable
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Items.Tools;
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

// Drum tile - definition-driven, with a thin rendering / interaction subclass:
// it draws the stored fluid on the drum face (PostDraw) and handles bucket +
// fluid-cell right-clicks. Same pattern as SuperTankTile, minus the tier.
public class DrumTile : TieredMachineTile
{
	public DrumTile() { }
	public DrumTile(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	protected override Color MapColor     => new(150, 150, 170);
	protected override int   MineDustType => DustID.Iron;

	// Drums don't fit MachineRenderer's casing+overlay scheme - install the
	// dedicated two-layer drum composite instead (DrumRenderer).
	public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
	{
		DrumRenderer.EnsureTileTexture(Type, _def?.MaterialId);
		return true;
	}

	public override void WarmUpTexture() =>
		DrumRenderer.EnsureTileTexture(Type, _def?.MaterialId);

	// Stored fluid on the drum face - one continuous draw covering the machine
	// square inset by a 4-art-px border. Mirrors SuperTankTile.PostDraw.
	public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
	{
		base.PostDraw(i, j, spriteBatch);
		if (!MachineCellResolver.TryFindAt<DrumMachine>(i, j, out var drum)) return;

		var (w, h) = drum.Size;
		int originX = drum.Position.X, originY = drum.Position.Y;
		// Draw once, from the bottom-right sub-tile (last cell drawn).
		if (i != originX + w - 1 || j != originY + h - 1) return;

		var stored = drum.GetTank(0);
		if (stored.IsEmpty || stored.Type is null) return;

		Vector2 zero = Main.drawToScreen ? Vector2.Zero
			: new Vector2(Main.offScreenRange, Main.offScreenRange);
		Vector2 pos = new Vector2(originX * 16 - (int)Main.screenPosition.X,
		                          originY * 16 - (int)Main.screenPosition.Y) + zero;
		const int Border = 8;
		var inner = new Rectangle((int)pos.X + Border, (int)pos.Y + Border,
		                          w * 16 - 2 * Border, h * 16 - 2 * Border);
		FluidIconRenderer.Draw(spriteBatch, stored.Type, inner, light: Lighting.GetColor(originX, originY));
	}

	private static readonly SoundStyle ScrewdriverSound =
		new("GregTechCEuTerraria/Content/Sounds/screwdriver") { Volume = 0.6f };

	public override bool RightClick(int i, int j)
	{
		if (!MachineCellResolver.TryFindAt<DrumMachine>(i, j, out var drum)) return false;

		var player = Main.LocalPlayer;
		var held = player.HeldItem;

		// Screwdriver-right-click -> toggle auto-output (port of
		// DrumMachine.onScrewdriverClick). Server-authoritative - mirrors
		// TransformerTile's screwdriver-flip path.
		if (IsScrewdriver(held))
		{
			SoundEngine.PlaySound(ScrewdriverSound, new Vector2(i * 16f, j * 16f));
			if (Main.netMode == NetmodeID.MultiplayerClient)
				DrumScrewdriverPacket.SendRequest(drum.Position.X, drum.Position.Y);
			else
				DrumScrewdriverPacket.Apply(drum, Main.myPlayer);
			return true;
		}

		// Bucket / fluid-cell-in-hand interaction was removed - it routed
		// through TankInteractPacket which had a vanilla-SyncEquipment-
		// ignore-self dupe bug (see SuperTankTile.RightClick for the full
		// story). Fluid transfer is now in-GUI through the FluidSlotWidget
		// (server-authoritative via FluidSlotAction + CursorUpdatePacket).
		MachineUISystem.OpenFor(drum, DrumLayout.Build(drum));
		return true;
	}

	// Held item is a (GregTech) screwdriver - plain or electric. Same check
	// TransformerTile uses.
	private static bool IsScrewdriver(Item item)
	{
		if (item?.ModItem is not ToolItem tool) return false;
		string n = tool.ToolType.Name;
		return n == "screwdriver" || n.EndsWith("_screwdriver");
	}
}
