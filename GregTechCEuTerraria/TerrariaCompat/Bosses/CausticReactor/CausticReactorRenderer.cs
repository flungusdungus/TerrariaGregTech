#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.CausticReactor;

// Bakes the Caustic Reactor body from upstream GregTech textures at warm-up and
// hands back two cached Texture2Ds for the boss PreDraw. Mirrors the real Large
// Chemical Reactor: a 3x3 box of chemically-inert (PTFE) machine casing with the
// reactor controller front face in the CENTRE block - NO wings (this boss is an
// anchored caster that teleports between arena positions, it doesn't fly):
//
//   CASING  CASING      CASING
//   CASING  CONTROLLER  CASING        <- inert PTFE casing (+chemical_reactor front overlay)
//   CASING  CASING      CASING
//
//   _body : the opaque machine face.
//   _glow : the emissive reactor face (centre controller), drawn brighter +
//           hue-shifted by the boss as it cycles reagents / enters phase 2.
//
// Caustic-Reactor-specific layout only; all pixel plumbing lives in BossArt.
internal static class CausticReactorRenderer
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
				_head = BossArt.BakeHeadAsset(_body, HeadW, HeadH, "caustic_reactor_head");
			return _head;
		}
	}

	private static void Ensure()
	{
		if (_tried || Main.dedServ) return;
		_tried = true;

		var casing   = BossArt.LoadFace(Root + "block/casings/solid/machine_casing_inert_ptfe", Face);
		var reactor  = BossArt.LoadFace(Root + "block/machines/chemical_reactor/overlay_front", Face);
		var reactorG = BossArt.LoadFace(Root + "block/machines/chemical_reactor/overlay_front_active_emissive", Face);
		if (casing is null) return; // required; overlays optional

		var body = new Color[GridW * GridH];
		var glow = new Color[GridW * GridH]; // transparent default

		for (int cy = 0; cy < Rows; cy++)
			for (int cx = 0; cx < Cols; cx++)
				BossArt.Stamp(body, GridW, casing, Face, cx, cy, over: false);

		// Centre block = the reactor controller face, with the emissive on glow.
		BossArt.Stamp(body, GridW, reactor, Face, 1, 1, over: true);
		BossArt.Stamp(glow, GridW, reactorG, Face, 1, 1, over: false);

		_body = BossArt.MakeTexture(BossArt.Upscale(body, GridW, GridH, 2), Width, Height);
		_glow = BossArt.MakeTexture(BossArt.Upscale(glow, GridW, GridH, 2), Width, Height);
	}
}
