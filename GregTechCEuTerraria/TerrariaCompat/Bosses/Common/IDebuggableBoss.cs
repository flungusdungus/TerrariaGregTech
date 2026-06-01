#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Opt-in interface for custom bosses that want their state surfaced through
// the GTConfig.DebugMobs overlay. Implementers expose:
//   * a per-frame text snapshot (BuildDebugLines) drawn in a screen-locked panel
//   * (optional) world-space gizmos (DrawDebugGizmos) overlaid in entity space -
//     movement anchors, target crosshairs, attack reticles, range circles
//
// Keep both methods cheap - called every draw frame while DebugMobs is on.
// Pre-format expensive state into runtime fields the boss already keeps and
// just read them here. Gizmos are routed through BossDebugGlobalNPC's PostDraw
// hook so the implementing class doesn't need its own draw plumbing.
public interface IDebuggableBoss
{
	// Append one string per displayable row. The first line is treated as a
	// header (rendered bigger / brighter) so put the boss name there.
	void BuildDebugLines(List<string> lines);

	// Default no-op. Override to draw movement-plan gizmos in world space using
	// `screenPos` as Main.screenPosition (subtract it from world coords to get
	// screen-relative draw positions). Use the helpers in DebugOverlaySystem
	// (DrawLine / DrawCircle / DrawCrosshair) for consistent visuals.
	void DrawDebugGizmos(SpriteBatch sb, Vector2 screenPos) { }

	// Human-readable name of the boss's CURRENT attack/state, used by the
	// BossFightTracker for the rolling timeline + export. Default returns the
	// raw integer; each boss should override with its own state->name mapper.
	string CurrentAttackLabel() => "?";

	// Current phase index (0 = base, 1 = phase 2, etc.). Used by the tracker
	// to log explicit "[phase2] enter at player HP X%" timeline entries.
	// Default returns 0; override to read your phase ai slot.
	int CurrentPhase() => 0;
}
