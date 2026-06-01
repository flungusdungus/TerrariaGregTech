#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Questbook;

// Loads Data/Questbook/questlog.json (emitted by port-questbook.py from the
// Community Pack's FTB Quests) at PostSetupContent and resolves item tasks
// through IngredientResolverImpl - same path as recipes.
//
// auto-check: every task is a resolvable item task -> completes from inventory.
// manual: anything else (checkmark / unresolved) -> player presses the button.
public sealed class QuestbookSystem : ModSystem
{
	public const string LocRoot = "Mods.GregTechCEuTerraria.Questbook";

	internal static QuestLogData Data { get; private set; } = new();
	internal static Dictionary<string, QuestData> QuestsById { get; private set; } = [];
	internal static Dictionary<string, ResolvedQuest> Resolved { get; private set; } = [];

	public override void PostSetupContent()
	{
		try
		{
			LoadQuestbook();
		}
		catch (Exception e)
		{
			Mod.Logger.Error("Questbook load failed", e);
		}
	}

	private void LoadQuestbook()
	{
		byte[] bytes = Mod.GetFileBytes("Data/Questbook/questlog.json");
		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		Data = JsonSerializer.Deserialize<QuestLogData>(bytes, options) ?? new QuestLogData();
		QuestsById = Data.Quests.ToDictionary(q => q.Id);

		IngredientResolverImpl resolver = IngredientResolverImpl.Instance;
		Resolved = [];

		foreach (QuestData quest in Data.Quests)
		{
			var resolved = new ResolvedQuest();
				bool anyItemResolved = false;

				foreach (TaskData task in quest.Tasks)
				{
					if (task.Type == "item")
					{
						// Accepts any of `Items` (FTB itemfilters:or flattened by
						// the porter) and/or any item in `Tag` (itemfilters:tag).
						var types = new List<int>();
						foreach (string id in task.Items)
						{
							int t = resolver.ResolveItemType(id);
							if (t > 0)
								types.Add(t);
						}
						if (!string.IsNullOrEmpty(task.Tag))
							foreach (int t in resolver.ResolveItemTag(task.Tag))
								if (t > 0)
									types.Add(t);

						int[] accept = types.Distinct().ToArray();
						resolved.Tasks.Add(new ResolvedTask
						{
							IsItem = true,
							AcceptTypes = accept,
							Count = Math.Max(1, task.Count),
							Label = task.Label,
						});
						if (accept.Length > 0)
							anyItemResolved = true;
					}
					else
					{
						resolved.Tasks.Add(new ResolvedTask { IsItem = false });
					}
				}

				// Auto-check once any item task resolved; unresolvable / checkmark
				// tasks are not gated on (quest completes from satisfiable rules).
				resolved.AutoCheck = anyItemResolved;

				// Explicit icon, else first resolved task's first acceptable item.
				resolved.IconType = resolver.ResolveItemType(quest.Icon);
				if (resolved.IconType <= 0)
					foreach (ResolvedTask t in resolved.Tasks)
						if (t.IsItem && t.AcceptTypes.Length > 0)
						{
							resolved.IconType = t.AcceptTypes[0];
							break;
						}

			Resolved[quest.Id] = resolved;
		}

		int total = Resolved.Count;
		int auto = Resolved.Values.Count(r => r.AutoCheck);
		Mod.Logger.Info($"Questbook: {Data.Chapters.Count} chapters, {total} quests "
			+ $"({auto} auto-check, {total - auto} manual)");
	}

	/// <summary>A quest whose completion is driven by the player's inventory.</summary>
	public static bool IsAutoCheck(string questId)
		=> Resolved.TryGetValue(questId, out ResolvedQuest? r) && r.AutoCheck;

	/// <summary>True when every resolvable item task of an auto-check quest is satisfied.</summary>
	public static bool CheckCompletion(string questId, Player player)
	{
		if (!Resolved.TryGetValue(questId, out ResolvedQuest? r) || !r.AutoCheck)
			return false;

		foreach (ResolvedTask task in r.Tasks)
			if (task.IsItem && task.AcceptTypes.Length > 0)
			{
				int have = 0;
				foreach (int type in task.AcceptTypes)
					have += CountInInventory(player, type);
				if (have < task.Count)
					return false;
			}

		return true;
	}

	/// <summary>Total stack count of <paramref name="itemType"/> across the player's inventory.</summary>
	public static int CountInInventory(Player player, int itemType)
	{
		int total = 0;
		foreach (Item item in player.inventory)
			if (item is { IsAir: false } && item.type == itemType)
				total += item.stack;

		return total;
	}
}

/// <summary>A quest's tasks resolved to concrete Terraria item types.</summary>
internal sealed class ResolvedQuest
{
	public bool AutoCheck;
	public int IconType;     // resolved Terraria item type for the node icon; 0 = none
	public List<ResolvedTask> Tasks = [];
}

/// <summary>One resolved task: an item requirement, or a manual checkmark.</summary>
internal sealed class ResolvedTask
{
	public bool IsItem;
	public int[] AcceptTypes = [];   // any of these item types satisfies the requirement
	public int Count = 1;
	public string Label = "";        // FTB-supplied display label (e.g. "Any Logs"); may be empty
}
