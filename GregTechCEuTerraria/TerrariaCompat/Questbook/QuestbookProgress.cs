#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.Achievements;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.Questbook;

// Per-player completion. Owning client polls own inventory; no netcode.
// Auto-check quests latch on first satisfaction (player can spend items
// afterwards); manual quests come from the UI button. A new completion fires
// vanilla's achievement banner via an ad-hoc Achievement whose FriendlyName
// resolves to a lazily-registered locale key - no Main.Achievements bloat.
public sealed class QuestbookProgress : ModPlayer
{
	private const string TagKey = "GregTechQuestbookCompleted";
	private const int CheckInterval = 30;

	public HashSet<string> Completed { get; private set; } = [];

	private int _checkTimer;

	public override void SaveData(TagCompound tag) => tag[TagKey] = Completed.ToList();

	public override void LoadData(TagCompound tag)
	{
		Completed = [];
		// Tolerate pre-pivot saves (old build stored int ids, TryGet<List<string>>
		// would throw); SaveData rewrites in the new format on next save.
		try
		{
			if (tag.TryGet(TagKey, out List<string> saved))
				Completed = [.. saved];
		}
		catch (IOException)
		{
		}
	}

	public override void PostUpdate()
	{
		if (Player.whoAmI != Main.myPlayer)
			return;

		if (++_checkTimer < CheckInterval)
			return;
		_checkTimer = 0;

		foreach (string id in QuestbookSystem.Resolved.Keys)
		{
			if (Completed.Contains(id))
				continue;

			if (QuestbookSystem.IsAutoCheck(id) && QuestbookSystem.CheckCompletion(id, Player))
				Complete(id);
		}
	}

	// Single transition point: banner fires only on a genuinely-new completion.
	// LoadData restores bypass this so prior completions don't re-banner on join.
	private void Complete(string questId)
	{
		if (!Completed.Add(questId))
			return;
		FireBanner(questId);

		// 50 gold reward - owning-client local (PostUpdate myPlayer-gated,
		// MarkManual local), grants exactly once and never on dedicated server.
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(
			Player, Player.GetSource_GiftOrReward(), ItemID.GoldCoin, 50);
	}

	/// <summary>Whether the local player has completed the given quest.</summary>
	public static bool IsComplete(string questId)
		=> Main.LocalPlayer.GetModPlayer<QuestbookProgress>().Completed.Contains(questId);

	/// <summary>Marks a manual (checkmark) quest complete for the local player.</summary>
	public static void MarkManual(string questId)
		=> Main.LocalPlayer.GetModPlayer<QuestbookProgress>().Complete(questId);

	private static void FireBanner(string questId)
	{
		if (Main.dedServ)
			return;

		// InGamePopups.AchievementUnlockedPopup reads FriendlyName once via
		// "Achievements.<n>_Name"; lazy-register with the quest's title.
		string key = "Achievements.GTQuest_" + questId + "_Name";
		string title = QuestbookSystem.QuestsById.TryGetValue(questId, out QuestData? q)
			? q.Title : questId;
		Language.GetOrRegister(key, () => title);

		var achievement = new Achievement("GTQuest_" + questId);
		InGameNotificationsTracker.AddCompleted(achievement);
		SoundEngine.PlaySound(SoundID.AchievementComplete);
	}
}
