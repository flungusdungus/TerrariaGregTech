#nullable enable
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress;

// Tracks whether the Implosion Press has been defeated in this world. Saved per
// world; available for future progression gating. Not net-synced on player join
// yet - nothing gates on it client-side. Mirrors CausticReactorWorld.
public class ImplosionPressWorld : ModSystem
{
	public static bool Downed;

	public static void MarkDowned() => Downed = true;

	public override void OnWorldLoad() => Downed = false;
	public override void OnWorldUnload() => Downed = false;

	public override void SaveWorldData(TagCompound tag)
	{
		if (Downed) tag["downedImplosionPress"] = true;
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Downed = tag.ContainsKey("downedImplosionPress");
	}
}
