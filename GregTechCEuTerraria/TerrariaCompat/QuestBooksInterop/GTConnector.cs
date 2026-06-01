#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using QuestBooks.QuestLog.DefaultElements;
using QuestBooks.Systems;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.QuestBooksInterop;

// Connector that adds a V-arrowhead at the destination end. Reuses QB's own
// DrawConnection helper for the arm rendering so the arms match the main
// line's thickness + texture exactly - no stretched-pixel rotation math of
// our own (the prior attempt at that produced flying rectangles all over
// the canvas).
//
// Subclassing keeps Source/Destination resolution and visibility checks
// untouched; we only layer two short line segments on top.
[ExtendsFromMod("QuestBooks")]
public sealed class GTConnector : Connector
{
	// Length of each arrowhead arm in canvas (logical) pixels - zoom is
	// applied separately at draw time, like the main line.
	private const float ArmLength = 28f;
	// Splay angle from the reverse-line direction in radians. ~30 degrees -
	// wide enough to read as an arrow, narrow enough to not overshoot.
	private const float ArmSplay = 0.55f;
	// Pull the arrowhead TIP back from the destination icon center so the V
	// sits in the empty space just outside the destination badge - otherwise
	// the arms anchor under the icon and disappear. Matches our IconSize/2
	// (16 logical px) plus a few px of breathing room.
	private const float TipPullback = 20f;

	public override void DrawToCanvas(SpriteBatch sb, Vector2 canvasOffset, float zoom, bool selected, bool hovered)
	{
		base.DrawToCanvas(sb, canvasOffset, zoom, selected, hovered);

		if (Source is null || Destination is null) return;

		// Same color rule the base uses so the arms match the line tint.
		Color color =
			selected ? Color.Red :
			hovered  ? Color.Yellow :
			Source.ConnectionActive(Destination) || QuestLogDrawer.ActiveStyle.UseDesigner
				? Color.White : Color.DimGray;

		Vector2 src = Source.ConnectorAnchor * zoom;
		Vector2 dst = Destination.ConnectorAnchor * zoom;
		Vector2 line = dst - src;
		if (line.LengthSquared() < 1f) return;

		// Tip sits just outside the destination icon (pulled back along the
		// line toward source). Each arm extends from the tip BACK toward the
		// source, splayed left and right by ArmSplay.
		Vector2 unit = Vector2.Normalize(line);
		Vector2 tip  = dst - unit * (TipPullback * zoom);
		float armLen = ArmLength * zoom;

		// "Backward" unit vector (toward source) rotated by +/-ArmSplay.
		Vector2 backLeft  = RotateUnit(-unit,  ArmSplay)  * armLen;
		Vector2 backRight = RotateUnit(-unit, -ArmSplay) * armLen;

		// DrawConnection draws a thick line between the two given points using
		// the same BigPixel + LineThickness setup as the main line.
		DrawConnection(sb, tip, tip + backLeft,  color, zoom);
		DrawConnection(sb, tip, tip + backRight, color, zoom);
	}

	private static Vector2 RotateUnit(Vector2 v, float radians)
	{
		float cos = System.MathF.Cos(radians);
		float sin = System.MathF.Sin(radians);
		return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
	}
}
