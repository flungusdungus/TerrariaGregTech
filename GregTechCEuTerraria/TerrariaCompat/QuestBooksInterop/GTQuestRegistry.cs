#nullable enable
using System.Collections.Concurrent;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Questbook;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.QuestBooksInterop;

// Quest id -> cached GTQuest instance, plus the data source. Lazy-init so
// instances only allocate when QB's UI actually opens (the QuestDisplay
// getter is called from draw paths and the info-page open path).
[ExtendsFromMod("QuestBooks")]
public static class GTQuestRegistry
{
	private static Mod? _ourMod;
	private static readonly ConcurrentDictionary<string, GTQuest> _cache = new();

	internal static void Bind(Mod ourMod) => _ourMod = ourMod;

	public static GTQuest Get(string id) =>
		_cache.GetOrAdd(id, BuildInstance);

	// GTQuest pulls title/desc/icon/tasks from QuestbookSystem on demand inside
	// MakeSimpleInfoPage, so the cache just hands back a thin wrapper keyed by id.
	private static GTQuest BuildInstance(string id) => new(id, _ourMod!);
}
