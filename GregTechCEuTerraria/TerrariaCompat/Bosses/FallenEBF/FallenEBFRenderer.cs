#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.FallenEBF;

// Bakes the Fallen EBF body from upstream GregTech textures at warm-up and
// hands back two cached Texture2Ds for the boss PreDraw:
//
//   CASING  MUFFLER     CASING        <- heatproof casing (+muffler overlay)
//   COIL    COIL        COIL          <- cupronickel coil
//   COIL    COIL        COIL
//   CASING  CONTROLLER  CASING        <- heatproof casing (+EBF front overlay)
//
//   _body : the opaque machine face.
//   _glow : the emissive layer (coil bloom + EBF active face) drawn brighter
//           as the boss overheats.
//
// EBF-specific layout only; all pixel plumbing + wing drawing lives in BossArt.
internal static class FallenEBFRenderer
{
	private const string Root = "GregTechCEuTerraria/Content/Textures/";
	private const int Face = 16;
	private const int Cols = 3, Rows = 4;
	private const int GridW = Cols * Face;   // 48
	private const int GridH = Rows * Face;   // 64

	// 2x upscaled dimensions (the baked textures' real size).
	public static int Width => GridW * 2;    // 96
	public static int Height => GridH * 2;   // 128

	// Compact boss-head icon size (3:4, matching the body aspect).
	private const int HeadW = 30, HeadH = 40;

	// Spectre Wings - the one vanilla wing whose 4-frame sheet layout is
	// confirmed in source; ghostly wings suit a "Fallen" machine.
	private const int WingId = (int)Terraria.ID.ArmorIDs.Wing.SpectreWings; // 11
	private const int WingFrames = 4;

	private static Texture2D? _body, _glow;
	private static Asset<Texture2D>? _head;
	private static Asset<Texture2D>? _bodyAsset;
	private static bool _tried;

	public static Texture2D? Body { get { Ensure(); return _body; } }
	public static Texture2D? Glow { get { Ensure(); return _glow; } }

	// Wrapped body Asset for places that need Asset<Texture2D> (e.g. ModMenu sun/moon).
	public static Asset<Texture2D>? BodyAsset
	{
		get
		{
			Ensure();
			if (_bodyAsset is null && _body is not null)
				_bodyAsset = MachineRenderer.WrapAsset(_body, "fallen_ebf_body");
			return _bodyAsset;
		}
	}

	// Boss-head icon = the baked body downscaled, wrapped as an Asset; the NPC
	// swaps this over the load-time placeholder on first draw.
	public static Asset<Texture2D>? BossHeadAsset
	{
		get
		{
			Ensure();
			if (_head is null && _body is not null)
				_head = BossArt.BakeHeadAsset(_body, HeadW, HeadH, "fallen_ebf_head");
			return _head;
		}
	}

	// Draw the flapping wing pair behind the core.
	public static void DrawWings(SpriteBatch sb, Vector2 center, Color color, int frame, float scale, float flap)
		=> BossArt.DrawWings(sb, center, color, WingId, WingFrames, frame, scale, flap, bodyHalfWidthPx: Width * 0.40f);

	private static void Ensure()
	{
		if (_tried || Main.dedServ) return;
		_tried = true;

		var casing   = BossArt.LoadFace(Root + "block/casings/solid/machine_casing_heatproof", Face);
		var coil     = BossArt.LoadFace(Root + "block/casings/coils/machine_coil_cupronickel", Face);
		var coilGlow = BossArt.LoadFace(Root + "block/casings/coils/machine_coil_cupronickel_bloom", Face);
		var muffler  = BossArt.LoadFace(Root + "block/overlay/machine/overlay_muffler", Face);
		var ebf      = BossArt.LoadFace(Root + "block/multiblock/electric_blast_furnace/overlay_front", Face);
		var ebfGlow  = BossArt.LoadFace(Root + "block/multiblock/electric_blast_furnace/overlay_front_active_emissive", Face);
		if (casing is null || coil is null) return; // required; rest are optional overlays

		var body = new Color[GridW * GridH];
		var glow = new Color[GridW * GridH]; // transparent default

		// Row 0: casing | casing+muffler | casing
		BossArt.Stamp(body, GridW, casing, Face, 0, 0, over: false);
		BossArt.Stamp(body, GridW, casing, Face, 1, 0, over: false);
		BossArt.Stamp(body, GridW, muffler, Face, 1, 0, over: true);
		BossArt.Stamp(body, GridW, casing, Face, 2, 0, over: false);

		// Rows 1-2: cupronickel coils (bloom -> glow layer)
		for (int cy = 1; cy <= 2; cy++)
			for (int cx = 0; cx < Cols; cx++)
			{
				BossArt.Stamp(body, GridW, coil, Face, cx, cy, over: false);
				BossArt.Stamp(glow, GridW, coilGlow, Face, cx, cy, over: false);
			}

		// Row 3: casing | casing+EBF controller | casing
		BossArt.Stamp(body, GridW, casing, Face, 0, 3, over: false);
		BossArt.Stamp(body, GridW, casing, Face, 1, 3, over: false);
		BossArt.Stamp(body, GridW, ebf, Face, 1, 3, over: true);
		BossArt.Stamp(glow, GridW, ebfGlow, Face, 1, 3, over: false);
		BossArt.Stamp(body, GridW, casing, Face, 2, 3, over: false);

		_body = BossArt.MakeTexture(BossArt.Upscale(body, GridW, GridH, 2), Width, Height);
		_glow = BossArt.MakeTexture(BossArt.Upscale(glow, GridW, GridH, 2), Width, Height);
	}
}
