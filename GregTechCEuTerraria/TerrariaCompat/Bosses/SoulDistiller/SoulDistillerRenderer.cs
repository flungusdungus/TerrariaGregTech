#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// Bakes the Soul Distiller's worm segments from upstream distillation-tower +
// clean-stainless casing textures, mirroring FallenEBFRenderer's grid-bake
// approach (BossArt.LoadFace -> BossArt.Stamp into a ColsxRows grid -> Upscale ->
// MakeTexture). Every segment is a 3-casings-wide slice (so the worm reads as a
// fat 3-plate-thick tower); the head's centre casing carries the distillation
// controller face + emissive glow, just like EBF's controller row.
//
// Segments are baked NEUTRAL (stainless); the per-fraction / per-segment colour
// is applied at draw time as the sprite tint, so one set of baked textures serves
// the parent worm (head->tail gradient) AND every fraction sub-worm (uniform).
//
//   HeadBody  : [casing][casing+controller][casing]
//   HeadGlow  : centre controller emissive (fraction-tinted glow)
//   BodyPlain : [casing][casing][casing]               (most segments)
//   BodyHatch : [casing][casing+pipe/hatch][casing]    (every 4th segment)
//   Tail      : [grate][grate][grate]                  (vented end-cap)
internal static class SoulDistillerRenderer
{
	private const string Root = "GregTechCEuTerraria/Content/Textures/";
	private const int Face = 16;
	private const int Cols = 3, Rows = 1;
	private const int Center = 1; // middle column of the 3-wide slice
	private const int GridW = Cols * Face; // 48
	private const int GridH = Rows * Face; // 16

	// Baked (2x-upscaled) segment dimensions, drawn at NPC.scale.
	public const int Width = GridW * 2;  // 96
	public const int Height = GridH * 2; // 32

	// Fraction ids (also the index into Fractions[]).
	public const int RefineryGas = 0, Naphtha = 1, LightOil = 2, HeavyOil = 3;
	public const int FractionCount = 4;

	// Light -> heavy, matching where each fraction sits in a real column (light
	// gases up top, heavy crude at the bottom). Head=light, tail=heavy.
	public static readonly Color[] Fractions =
	{
		new(150, 205, 225), // refinery gas - pale cyan
		new(225, 210, 100), // naphtha      - bright yellow
		new(205, 150, 70),  // light oil    - amber
		new(110, 65, 50),   // heavy oil    - dark crude brown
	};

	private static Texture2D? _headBody, _headGlow, _bodyPlain, _bodyHatch, _tail;
	private static Asset<Texture2D>? _head;
	private static bool _tried;

	public static Texture2D? HeadBody  { get { Ensure(); return _headBody; } }
	public static Texture2D? HeadGlow  { get { Ensure(); return _headGlow; } }
	public static Texture2D? BodyPlain { get { Ensure(); return _bodyPlain; } }
	public static Texture2D? BodyHatch { get { Ensure(); return _bodyHatch; } }
	public static Texture2D? Tail      { get { Ensure(); return _tail; } }

	public static Asset<Texture2D>? BossHeadAsset
	{
		get
		{
			Ensure();
			if (_head is null && _headBody is not null)
				_head = BossArt.BakeHeadAsset(_headBody, 32, 16, "soul_distiller_head");
			return _head;
		}
	}

	// Colour for a segment at body-ratio `t` (0 = head/light .. 1 = tail/heavy).
	public static Color GradientColor(float t)
	{
		t = MathHelper.Clamp(t, 0f, 1f) * (FractionCount - 1);
		int i = (int)t;
		if (i >= FractionCount - 1) return Fractions[FractionCount - 1];
		return Color.Lerp(Fractions[i], Fractions[i + 1], t - i);
	}

	// Multiply a fraction tint by the ambient draw light (keeps lighting intact).
	public static Color Tint(Color light, Color frac) => new(
		(byte)(frac.R * light.R / 255),
		(byte)(frac.G * light.G / 255),
		(byte)(frac.B * light.B / 255),
		light.A);

	private static void Ensure()
	{
		if (_tried || Main.dedServ) return;
		_tried = true;

		Color[]? casing = BossArt.LoadFace(Root + "block/casings/solid/machine_casing_clean_stainless_steel", Face);
		Color[]? pipe   = BossArt.LoadFace(Root + "block/overlay/machine/overlay_pipe", Face);
		Color[]? front  = BossArt.LoadFace(Root + "block/multiblock/distillation_tower/overlay_front", Face);
		Color[]? glow   = BossArt.LoadFace(Root + "block/multiblock/distillation_tower/overlay_front_emissive", Face);
		Color[]? grate  = BossArt.LoadFace(Root + "block/casings/pipe/machine_casing_grate", Face);
		if (casing is null) return; // required

		_headBody  = BakeStrip(casing, centerOverlay: front);
		_headGlow  = BakeStrip(null,   centerOverlay: glow);
		_bodyPlain = BakeStrip(casing, centerOverlay: null);
		_bodyHatch = BakeStrip(casing, centerOverlay: pipe);
		_tail      = BakeStrip(grate ?? casing, centerOverlay: null);
	}

	// Bake one 3-wide slice: `baseFace` (or transparent) across all Cols, with an
	// optional overlay stamped on the centre column. Upscaled 2x. Same Stamp/
	// Upscale/MakeTexture pipeline as FallenEBFRenderer.
	private static Texture2D BakeStrip(Color[]? baseFace, Color[]? centerOverlay)
	{
		var px = new Color[GridW * GridH];
		for (int cx = 0; cx < Cols; cx++)
			BossArt.Stamp(px, GridW, baseFace, Face, cx, 0, over: false);
		BossArt.Stamp(px, GridW, centerOverlay, Face, Center, 0, over: true);
		return BossArt.MakeTexture(BossArt.Upscale(px, GridW, GridH, 2), Width, Height);
	}
}
