#nullable enable
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.FallenEBF;

// Tracks whether the Fallen EBF has been defeated in this world. Gates EBF-chan's
// arrival (she wanders the surface only after the Fallen EBF is down). The gate is
// read server-side only (NPC spawning + housing are server-authoritative), so this
// flag is NOT net-synced - matching SoulDistillerWorld / CausticReactorWorld. Add a
// sync packet if a client-side gate (UI/bestiary text) ever needs it.
public class FallenEBFWorld : ModSystem
{
	public static bool Downed;

	public static void MarkDowned() => Downed = true;

	public override void OnWorldLoad() => Downed = false;
	public override void OnWorldUnload() => Downed = false;

	public override void SaveWorldData(TagCompound tag)
	{
		if (Downed) tag["downedFallenEBF"] = true;
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Downed = tag.ContainsKey("downedFallenEBF");
	}
}
