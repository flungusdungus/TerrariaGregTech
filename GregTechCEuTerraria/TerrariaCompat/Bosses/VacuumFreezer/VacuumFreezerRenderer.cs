#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.VacuumFreezer;

// Bakes the Vacuum Freezer body (cached _body + _glow) for the boss PreDraw.
// 3x3 frostproof casing with the freezer controller face in the centre:
//
//   CASING  CASING      CASING
//   CASING  CONTROLLER  CASING
//   CASING  CASING      CASING
//
// _glow = emissive frost-bloom on the centre face, brighter in phase 2.
// Pixel plumbing + wing drawing live in BossArt.
internal static class VacuumFreezerRenderer
{
	private const string Root = "GregTechCEuTerraria/Content/Textures/";
	private const int Face = 16;
	private const int Cols = 3, Rows = 3;
	private const int GridW = Cols * Face;   // 48
	private const int GridH = Rows * Face;   // 48

	public static int Width => GridW * 2;    // 96
	public static int Height => GridH * 2;   // 96

	private const int HeadW = 36, HeadH = 36;

	// Every vanilla wing sheet is 4-frame (PlayerDrawLayers reads Height()/4).
	private const int WingId = (int)Terraria.ID.ArmorIDs.Wing.FrozenWings; // 10
	private const int WingFrames = 4;

	private static Texture2D? _body, _glow;
	private static Asset<Texture2D>? _head;
	private static Asset<Texture2D>? _bodyAsset;
	private static bool _tried;

	public static Texture2D? Body { get { Ensure(); return _body; } }
	public static Texture2D? Glow { get { Ensure(); return _glow; } }

	// Body wrapped as Asset<Texture2D> (e.g. ModMenu sun/moon).
	public static Asset<Texture2D>? BodyAsset
	{
		get
		{
			Ensure();
			if (_bodyAsset is null && _body is not null)
				_bodyAsset = MachineRenderer.WrapAsset(_body, "vacuum_freezer_body");
			return _bodyAsset;
		}
	}

	// Baked body downscaled; the NPC swaps it over the load-time placeholder head.
	public static Asset<Texture2D>? BossHeadAsset
	{
		get
		{
			Ensure();
			if (_head is null && _body is not null)
				_head = BossArt.BakeHeadAsset(_body, HeadW, HeadH, "vacuum_freezer_head");
			return _head;
		}
	}

	public static void DrawWings(SpriteBatch sb, Vector2 center, Color color, int frame, float scale, float flap)
		=> BossArt.DrawWings(sb, center, color, WingId, WingFrames, frame, scale, flap, bodyHalfWidthPx: Width * 0.40f);

	private static void Ensure()
	{
		if (_tried || Main.dedServ) return;
		_tried = true;

		var casing    = BossArt.LoadFace(Root + "block/casings/solid/machine_casing_frost_proof", Face);
		var freezer   = BossArt.LoadFace(Root + "block/multiblock/vacuum_freezer/overlay_front", Face);
		var freezerGl = BossArt.LoadFace(Root + "block/multiblock/vacuum_freezer/overlay_front_active_emissive", Face);
		if (casing is null) return; // required; rest are optional overlays

		var body = new Color[GridW * GridH];
		var glow = new Color[GridW * GridH];

		for (int cy = 0; cy < Rows; cy++)
			for (int cx = 0; cx < Cols; cx++)
				BossArt.Stamp(body, GridW, casing, Face, cx, cy, over: false);

		// Centre = controller face + emissive frost-bloom on the glow layer.
		BossArt.Stamp(body, GridW, freezer, Face, 1, 1, over: true);
		BossArt.Stamp(glow, GridW, freezerGl, Face, 1, 1, over: false);

		_body = BossArt.MakeTexture(BossArt.Upscale(body, GridW, GridH, 2), Width, Height);
		_glow = BossArt.MakeTexture(BossArt.Upscale(glow, GridW, GridH, 2), Width, Height);
	}
}
