#nullable enable
using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Graphics;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// Swaps the sprite drawn for a Gregith-spawned FinalFractal. Everything else
// (AI_182, rainbow trail hardcoded at Main.cs:28080, orbit, dust, lighting)
// is verbatim vanilla - only PreDraw is intercepted.
public sealed class GregithProjectileGlobal : GlobalProjectile
{
	public override bool InstancePerEntity => true;

	public int ToolItemId; // 0 = not a Gregith projectile -> vanilla draw

	// Set BEFORE NewProjectile so the synchronous SyncProjectile carries it
	// via SendExtraAI (post-hoc proj.netUpdate isn't reliable for AI_182).
	[ThreadStatic] private static int _pendingToolItemId;
	public static void SetPendingToolItemId(int id) => _pendingToolItemId = id;

	public override bool AppliesToEntity(Projectile entity, bool lateInstantiation) =>
		entity.type == ProjectileID.FinalFractal;

	public override void OnSpawn(Projectile projectile, IEntitySource source)
	{
		if (_pendingToolItemId > 0)
		{
			ToolItemId = _pendingToolItemId;
			_pendingToolItemId = 0;
		}
	}

	public override bool PreDraw(Projectile projectile, ref Color lightColor)
	{
		if (ToolItemId <= 0 || ToolItemId >= TextureAssets.Item.Length)
			return true;

		// Tool icon bake is lazy via ToolItem PreDraw/HoldItem; force it now
		// so a Gregith firing before any draw doesn't grab a placeholder.
		if (ContentSamples.ItemsByType.TryGetValue(ToolItemId, out var sample)
			&& sample.ModItem is ToolItem tool)
			tool.EnsureTextureBaked();

		// Returning false below skips tML's inner draw block AND the
		// Main.cs:28080 trail draw (`type == 933 || == 1100`), so we run the
		// rainbow strip manually. AI_182's dust is unaffected.
		default(FinalFractalHelper).Draw(projectile);

		var tex = TextureAssets.Item[ToolItemId].Value;
		Vector2 origin = new(tex.Width / 2f, tex.Height / 2f);
		SpriteEffects effects = projectile.spriteDirection < 0
			? SpriteEffects.FlipHorizontally : SpriteEffects.None;
		// 2x scale for visual parity with the rainbow trail (tool icons are
		// baked at 32 px native vs the much larger vanilla Zenith sprite).
		Main.EntitySpriteDraw(tex, projectile.Center - Main.screenPosition, null,
			lightColor * projectile.Opacity, projectile.rotation, origin, 2f, effects, 0);
		return false;
	}

	public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, BinaryWriter binaryWriter)
		=> binaryWriter.Write(ToolItemId);

	public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, BinaryReader binaryReader)
		=> ToolItemId = binaryReader.ReadInt32();
}
