#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress;

// Bakes the Implosion Press body from upstream GregTech textures at warm-up and
// hands back two cached Texture2Ds for the boss PreDraw. Mirrors the real
// Implosion Compressor multiblock, with the deliberate asymmetric vertical
// layout the user spec'd:
//
//   CASING  MUFFLER     CASING        <- top row: solid steel + muffler overlay top-centre
//   CASING  CASING      CASING        <- middle row: all solid steel
//   CASING  CONTROLLER  CASING        <- bottom row: solid steel + implosion_compressor face bottom-centre
//
//   _body : the opaque machine face (top-vented, bottom-emitting silhouette).
//   _glow : the emissive controller face (drawn brighter on telegraphs + phase 2).
//
// Implosion-Press-specific layout only; all pixel plumbing lives in BossArt.
// NO wings (this boss is an anchored heavy press, it doesn't fly).
internal static class ImplosionPressRenderer
{
	private const string Root = "GregTechCEuTerraria/Content/Textures/";
	private const int Face = 16;
	private const int Cols = 3, Rows = 3;
	private const int GridW = Cols * Face;   // 48
	private const int GridH = Rows * Face;   // 48

	// 2x upscaled dimensions (the baked textures' real size).
	public static int Width => GridW * 2;    // 96
	public static int Height => GridH * 2;   // 96

	private const int HeadW = 36, HeadH = 36;

	private static Texture2D? _body, _glow;
	private static Asset<Texture2D>? _head;
	private static bool _tried;

	public static Texture2D? Body { get { Ensure(); return _body; } }
	public static Texture2D? Glow { get { Ensure(); return _glow; } }

	public static Asset<Texture2D>? BossHeadAsset
	{
		get
		{
			Ensure();
			if (_head is null && _body is not null)
				_head = BossArt.BakeHeadAsset(_body, HeadW, HeadH, "implosion_press_head");
			return _head;
		}
	}

	private static void Ensure()
	{
		if (_tried || Main.dedServ) return;
		_tried = true;

		var casing       = BossArt.LoadFace(Root + "block/casings/solid/machine_casing_solid_steel", Face);
		var controller   = BossArt.LoadFace(Root + "block/multiblock/implosion_compressor/overlay_front", Face);
		var controllerGl = BossArt.LoadFace(Root + "block/multiblock/implosion_compressor/overlay_front_active_emissive", Face);
		var muffler      = BossArt.LoadFace(Root + "block/overlay/machine/overlay_muffler", Face);
		if (casing is null) return; // required; overlays optional

		var body = new Color[GridW * GridH];
		var glow = new Color[GridW * GridH]; // transparent default

		// Fill the whole 3x3 box with solid-steel casing.
		for (int cy = 0; cy < Rows; cy++)
			for (int cx = 0; cx < Cols; cx++)
				BossArt.Stamp(body, GridW, casing, Face, cx, cy, over: false);

		// Top-centre: muffler overlay (vents pressure smoke between attacks).
		BossArt.Stamp(body, GridW, muffler, Face, 1, 0, over: true);

		// Bottom-centre: implosion compressor controller face + emissive on glow.
		BossArt.Stamp(body, GridW, controller, Face, 1, 2, over: true);
		BossArt.Stamp(glow, GridW, controllerGl, Face, 1, 2, over: false);

		_body = BossArt.MakeTexture(BossArt.Upscale(body, GridW, GridH, 2), Width, Height);
		_glow = BossArt.MakeTexture(BossArt.Upscale(glow, GridW, GridH, 2), Width, Height);
	}
}
