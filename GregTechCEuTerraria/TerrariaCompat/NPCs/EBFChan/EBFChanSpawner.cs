#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.NPCs.EBFChan;

// Spawns EBF-chan at the Fallen EBF's death site. Called from FallenEBF.OnKill
// (server/SP). Like the Goblin Tinkerer after you free him, she arrives as a
// homeless town NPC and simply WANDERS until housed - she does NOT despawn and
// there's no departure message (faithful to vanilla rescued-NPC behavior). If she
// dies, kill the Fallen EBF again and she re-emerges from the wreckage. Once you
// build her a valid house she settles in, and the normal town-NPC spawn
// (CanTownNPCSpawn, gated on FallenEBFWorld.Downed) respawns her there on death.
public static class EBFChanSpawner
{
	// Spawn at the boss's death position if she isn't already in the world. Mirrors
	// WorldGen.SpawnTownNPC's homeless-arrival path (WorldGen.cs:5117-5135).
	public static void SpawnFromBossDeath(Vector2 worldCenter)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient)
			return;

		int type = ModContent.NPCType<EBFChanNPC>();
		if (NPC.AnyNPCs(type))
			return; // already wandering or housed - don't duplicate

		int x = (int)worldCenter.X;
		int y = (int)worldCenter.Y;
		// GetSpawnSourceForTownSpawn is internal in tML; EntitySource_SpawnNPC is the
		// public equivalent vanilla uses under the hood.
		int who = NPC.NewNPC(new EntitySource_SpawnNPC(), x, y, type, 1);
		if (who < 0 || who >= Main.maxNPCs)
			return;

		NPC npc = Main.npc[who];
		npc.homeless = true;
		npc.netUpdate = true;
		// Despawn-when-alone (vanilla's homelessDespawn field isn't exposed to mods,
		// so EBFChanNPC.AI replicates UnspawnHomelessNPC's behavior while homeless).
		WorldGen.QuickFindHome(who); // settle into a valid house immediately if one exists

		Announce("EBF-chan emerges from the wreckage. Build her a house before she wanders off!");
	}

	private static void Announce(string text)
	{
		var color = new Color(255, 180, 90);
		if (Main.netMode == NetmodeID.Server)
			ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(text), color);
		else
			Main.NewText(text, color.R, color.G, color.B);
	}
}
