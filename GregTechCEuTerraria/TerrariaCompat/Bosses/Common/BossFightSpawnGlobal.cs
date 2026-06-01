#nullable enable
using GregTechCEuTerraria.Config;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Feeds BossFightTracker.RecordSpawn for any hostile-only projectile (the
// filter we use for "threat projectiles" - matches every boss-spawned bullet
// in this mod). Combined with the tracker's per-tick concurrent scan + the
// damage-taken histogram, this gives a damage-per-spawn cross-reference in
// the export.
//
// Skipped when DebugMobs is off or when no fight is active.
public class BossFightSpawnGlobal : GlobalProjectile
{
	public override bool InstancePerEntity => false;

	public override void OnSpawn(Projectile projectile, IEntitySource source)
	{
		if (!GTConfig.Instance.DebugMobs) return;
		if (!projectile.hostile || projectile.friendly) return;
		if (!BossFightTracker.FightActive) return;
		string name = projectile.ModProjectile?.Name ?? $"vanilla:{projectile.type}";
		BossFightTracker.RecordSpawn(name);
	}
}
