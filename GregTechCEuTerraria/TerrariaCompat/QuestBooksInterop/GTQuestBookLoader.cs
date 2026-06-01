#nullable enable
using System.Collections.Generic;
using System.Text;
using GregTechCEuTerraria.TerrariaCompat.Questbook;
using Newtonsoft.Json;
using QuestBooks;
using QuestBooks.QuestLog;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.QuestBooksInterop;

// Registers our questbook with QuestBooks. Runs at PostSetupContent so it
// fires AFTER QuestbookSystem (which loads our raw questlog.json), AFTER
// QuestBooks' own PostSetupContent (which calls EnableDesigner and stages
// the JSON-deserialization pipeline), and BEFORE QuestLoader.PostSetupRecipes
// (which calls SelectLogStyle and rebuilds ActiveQuests).
//
// Load() vs PostSetupContent(): we use PostSetupContent because we need
// QuestbookSystem.QuestsById populated AND QuestBooksMod.Instance reachable.
// Both are ready by this hook on both client and server.
[ExtendsFromMod("QuestBooks")]
public sealed class GTQuestBookLoader : ModSystem
{
	public override void PostSetupContent()
	{
		// Defense-in-depth: we already have [ExtendsFromMod("QuestBooks")] so
		// this whole type is skipped without QB. The runtime guard catches any
		// late-disable edge cases.
		if (!ModLoader.HasMod("QuestBooks")) return;

		GTQuestRegistry.Bind(Mod);

		// Register the locale keys our qb-questlog.json references. The
		// underlying titles already live in our parsed QuestbookSystem.Data as
		// raw English strings (the porter writes them to questlog.json
		// directly, not via tML's localization pipeline). QB calls
		// `Language.GetOrRegister(NameKey).Value` to resolve book/chapter
		// titles, so we pre-register those keys here.
		Language.GetOrRegister("Mods.GregTechCEuTerraria.QBBook.Name",
			() => string.IsNullOrEmpty(QuestbookSystem.Data.Pack)
				? "GregTech CEu"
				: QuestbookSystem.Data.Pack);
		foreach (var chapter in QuestbookSystem.Data.Chapters)
		{
			string key = $"Mods.GregTechCEuTerraria.QBBook.Chapter.{chapter.Key}";
			string title = chapter.Title;  // capture - closure can't read ref-typed iter var
			Language.GetOrRegister(key, () => title);
		}

		// Do NOT register BasicQuestLogStyle here - QB's VanillaQuestBooks
		// already registers it under their Mod, and QuestLoader.PostAddRecipes
		// (line 75) flattens LogStyleRegistry across mods into a
		// `Dictionary<string, QuestLogStyle>` keyed by style.Key. A second
		// BasicQuestLogStyle instance produces a duplicate "DefaultQuestLog"
		// key and ArgumentException-disables both mods.

		byte[] bytes = Mod.GetFileBytes("Data/Questbook/qb-questlog.json");
		string json = Encoding.UTF8.GetString(bytes);

		// Deserialize via OUR binder (see GTQBJsonBinder header for why). The
		// string overload of AddQuestLog runs QB's JsonTypeResolverFix which
		// can't resolve our assembly from inside QB's load context; the
		// IList<QuestBook> overload skips JSON entirely.
		var settings = new JsonSerializerSettings
		{
			TypeNameHandling           = TypeNameHandling.All,
			PreserveReferencesHandling = PreserveReferencesHandling.All,
			ReferenceLoopHandling      = ReferenceLoopHandling.Serialize,
			SerializationBinder        = new GTQBJsonBinder(),
		};
		var books = JsonConvert.DeserializeObject<List<QuestBook>>(json, settings);
		if (books is null)
		{
			Mod.Logger.Error("GTQuestBookLoader: failed to deserialize qb-questlog.json");
			return;
		}
		int chapterCount = 0;
		int questCount = 0;
		foreach (var b in books)
		{
			if (b?.Chapters == null) continue;
			chapterCount += b.Chapters.Count;
			foreach (var ch in b.Chapters)
				if (ch?.Elements != null)
					questCount += ch.Elements.Count;
		}
		Mod.Logger.Info(
			$"GTQuestBookLoader: AddGlobalQuestBooks \"GregTechCEu\" books={books.Count} chapters={chapterCount} elements={questCount}");
		// AddGlobalQuestBooks vs AddQuestLog: global books get concatenated
		// into EVERY selected QuestLog (see QuestManager.SelectQuestLog ->
		// `globalBooks.SelectMany`). Since QB ships only the "Terraria" log,
		// our TabBook ends up as a sibling cover inside it - the player sees
		// one log, one log-selector entry, and our content appears alongside
		// Terraria's books in the book carousel.
		QuestBooksMod.AddGlobalQuestBooks("GregTechCEu", books, Mod);
	}
}
