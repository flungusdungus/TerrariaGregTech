#nullable enable
using GregTechCEuTerraria.Config;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Feeds BossFightTracker with damage taken by the LOCAL player from hostile
// projectiles. Source name = ModProjectile.Name (or vanilla:<type>). Skipped
// when DebugMobs is off, or when the hurt player isn't the local one (each
// player's own client tracks their own fight log).
public class BossFightGlobalPlayer : ModPlayer
{
	public override void OnHitByProjectile(Projectile proj, Player.HurtInfo info)
	{
		if (!GTConfig.Instance.DebugMobs) return;
		if (Player.whoAmI != Main.myPlayer) return;
		string source = proj.ModProjectile?.Name ?? $"vanilla:{proj.type}";
		BossFightTracker.RecordPlayerHit(source, info.Damage);
	}

	public override void OnHitByNPC(NPC npc, Player.HurtInfo info)
	{
		if (!GTConfig.Instance.DebugMobs) return;
		if (Player.whoAmI != Main.myPlayer) return;
		string source = npc.ModNPC?.Name is { } n ? $"contact:{n}" : $"contact:vanilla:{npc.type}";
		BossFightTracker.RecordPlayerHit(source, info.Damage);
	}
}
