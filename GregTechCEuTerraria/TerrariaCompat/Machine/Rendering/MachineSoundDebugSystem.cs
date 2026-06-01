#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.UI.Profiler;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Loop-sound housekeeping (per-tick Sweep, ClearWorld silence) + profiler-
// gated text gizmo. Inactive sources draw red (ghost suspects).
public sealed class MachineSoundDebugSystem : ModSystem
{
	public override void PostUpdateEverything()
	{
		if (Main.dedServ) return;
		MachineLoopSoundRegistry.Sweep();
	}

	// Runs on world load AND unload.
	public override void ClearWorld()
	{
		if (Main.dedServ) return;
		MachineLoopSoundRegistry.ClearAll();
	}

	public override void PostDrawTiles()
	{
		if (Main.dedServ || !ProfilerUISystem.IsOpen) return;
		MachineLoopSoundRegistry.Sweep();
		if (MachineLoopSoundRegistry.Active.Count == 0) return;

		var sb = Main.spriteBatch;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
			Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null,
			Main.GameViewMatrix.TransformationMatrix);

		var font = FontAssets.MouseText.Value;
		const float scale = 0.7f;
		const float lineH = 14f;

		foreach (var tracker in MachineLoopSoundRegistry.Active)
		{
			var m = tracker.Machine;
			bool working = m.IsActive;
			Color color = working ? Color.Lime : Color.OrangeRed;

			// Anchor over footprint centre, lifted above the tile.
			float wx = m.Position.X * 16f + m.Size.Width * 8f;
			float wy = m.Position.Y * 16f;
			var screen = new Vector2(wx, wy) - Main.screenPosition;

			string[] lines =
			{
				m.DisplayName,
				$"{m.MachineKey} id={m.ID}",
				$"({m.Position.X},{m.Position.Y}) active={working}",
			};

			for (int i = 0; i < lines.Length; i++)
			{
				string text = lines[i];
				Vector2 size = font.MeasureString(text) * scale;
				Vector2 pos = new(screen.X - size.X * 0.5f, screen.Y - 48f + i * lineH);
				Terraria.Utils.DrawBorderString(sb, text, pos, color, scale);
			}
		}

		sb.End();
	}
}
