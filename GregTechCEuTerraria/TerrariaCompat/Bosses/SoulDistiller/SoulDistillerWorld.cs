#nullable enable
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// Tracks whether the Soul Distiller has been defeated in this world. Saved per
// world; available for future progression gating (recipes / drops behind it).
// NOTE: not yet net-synced on player join - nothing gates on it client-side yet;
// add a sync packet when the first gate lands.
public class SoulDistillerWorld : ModSystem
{
	public static bool Downed;

	public static void MarkDowned() => Downed = true;

	public override void OnWorldLoad() => Downed = false;
	public override void OnWorldUnload() => Downed = false;

	public override void SaveWorldData(TagCompound tag)
	{
		if (Downed) tag["downedSoulDistiller"] = true;
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Downed = tag.ContainsKey("downedSoulDistiller");
	}
}
