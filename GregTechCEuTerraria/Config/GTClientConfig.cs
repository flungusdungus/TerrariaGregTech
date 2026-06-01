#nullable enable
using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace GregTechCEuTerraria.Config;

// Per-client display preferences (each player decides for themselves, never
// synced). ClientSide so it lives in the player's own config file.
public sealed class GTClientConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ClientSide;

	// Shows the clickable "help with playtesting/feedback" Discord invite at the
	// top of the screen while in-game. On by default during the playtest period.
	[DefaultValue(true)]
	public bool ShowDiscordInvite { get; set; } = true;

	public static GTClientConfig Instance => ModContent.GetInstance<GTClientConfig>();
}
