#nullable enable
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

// Crate tile - definition-driven, with a thin rendering / interaction subclass:
// the dedicated CrateRenderer composite, and a right-click that either seals
// the crate (held tape) or opens its inventory grid.
public class CrateTile : TieredMachineTile
{
	public CrateTile() { }
	public CrateTile(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	protected override Color MapColor     => new(170, 140, 95);
	protected override int   MineDustType => DustID.WoodFurniture;

	// Crates don't fit MachineRenderer's casing+overlay scheme - install the
	// dedicated single-layer crate composite instead (CrateRenderer).
	public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
	{
		CrateRenderer.EnsureTileTexture(Type, _def?.MaterialId);
		return true;
	}

	public override void WarmUpTexture() =>
		CrateRenderer.EnsureTileTexture(Type, _def?.MaterialId);

	public override bool RightClick(int i, int j)
	{
		if (!MachineCellResolver.TryFindAt<CrateMachine>(i, j, out var crate)) return false;

		var player = Main.LocalPlayer;
		var held = player.HeldItem;

		// Held duct / basic tape on an un-taped crate -> seal it. Server-
		// authoritative - the client asks, the server consumes the tape +
		// re-syncs. Mirrors SuperTankTile's bucket path.
		if (!crate.IsTaped && CrateTapePacket.IsTape(held))
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
				CrateTapePacket.SendRequest(crate.Position.X, crate.Position.Y);
			else
				CrateTapePacket.Apply(crate, Main.myPlayer);
			SoundEngine.PlaySound(SoundID.Item50, player.position);
			return true;
		}

		// Otherwise -> open the inventory grid.
		MachineUISystem.OpenFor(crate, CrateLayout.Build(crate));
		return true;
	}
}
