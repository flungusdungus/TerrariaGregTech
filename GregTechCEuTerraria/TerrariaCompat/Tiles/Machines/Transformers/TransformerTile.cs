#nullable enable
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Items.Tools;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Transformers;

// Tile for the Transformer machine - ONE definition-driven class for all four
// baseAmp variants across all tiers (registered per (def x tier) by the
// factory). The 2x2 face is split: upper row HV, lower row LV. The DOWN state
// is baked into the per-type base sheet; a transform-UP transformer paints the
// UP-state art per cell in PostDraw.
public class TransformerTile : TieredMachineTile
{
	public TransformerTile() { }
	public TransformerTile(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	protected override Color MapColor => new(120, 110, 150);

	// baseAmp (1 / 2 / 4 / 16) - needed to bake the tile sheet before any
	// entity exists (the placement ghost has no tile entity).
	private int BaseAmp => _def!.BaseAmp;

	// Bake the DOWN-state 2-face sheet instead of the inherited casing+overlay
	// composite (the transformer has no block/machines/transformer_* texture).
	public override void WarmUpTexture() =>
		MachineRenderer.EnsureTransformerTile(Type, TileTier, BaseAmp);

	public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
	{
		MachineRenderer.EnsureTransformerTile(Type, TileTier, BaseAmp);
		return true;
	}

	private static readonly SoundStyle ScrewdriverSound =
		new("GregTechCEuTerraria/Content/Sounds/screwdriver") { Volume = 0.6f };

	// Right-click with a screwdriver flips the transform direction. Without a
	// screwdriver the transformer has no interaction (upstream parity - no GUI).
	public override bool RightClick(int i, int j)
	{
		if (!MachineCellResolver.TryFindAt<TransformerMachine>(i, j, out var tr)) return false;
		if (!IsScrewdriver(Main.LocalPlayer.HeldItem)) return false;

		SoundEngine.PlaySound(ScrewdriverSound, new Vector2(i * 16f, j * 16f));

		if (Main.netMode == NetmodeID.MultiplayerClient)
			TransformerTogglePacket.SendRequest(tr.Position.X, tr.Position.Y);
		else
			TransformerTogglePacket.Apply(tr, Main.myPlayer);
		return true;
	}

	private static bool IsScrewdriver(Item item)
	{
		if (item?.ModItem is not ToolItem tool) return false;
		string n = tool.ToolType.Name;
		return n == "screwdriver" || n.EndsWith("_screwdriver");
	}

	// The base sheet is the DOWN state; only a transform-UP transformer needs
	// the per-cell override.
	public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
	{
		if (!MachineCellResolver.TryFindAt<TransformerMachine>(i, j, out var tr)) return;
		if (!tr.IsTransformUp) return;
		MachineRenderer.DrawTransformerUpFace(spriteBatch, i, j, tr.Tier, tr.BaseAmp,
			i - tr.Position.X, j - tr.Position.Y);
	}
}
