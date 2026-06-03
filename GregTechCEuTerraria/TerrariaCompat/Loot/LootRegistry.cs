#nullable enable
using System.Collections.Generic;
using System.Text;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Loot;

// Aggregates "how does the player obtain this item from VANILLA Terraria sources"
// for the recipe browser's Loot mode. Three streams:
//
//   - NPC drops + treasure bags + crates (walked via Main.ItemDropsDB +
//     IItemDropRule.ReportDroprates, the same path the Bestiary uses).
//   - NPC shops (NPCShopDatabase.AllShops - every Entry's Item + Conditions).
//   - Shimmer transmutations (ItemID.Sets.ShimmerTransformToItem).
//
// Built once on first read and cached for the session. Cheap (a few thousand
// rows) so we keep the full list in memory and let the browser filter inline.
public static class LootRegistry
{
	public enum LootKind { NpcDrop, Shop, Shimmer }

	// One row per (source, target) pair. Source icon is EITHER an item id
	// (banner for enemy NPCs, shimmer source item, opened-container item)
	// OR a town-NPC head-texture index (shops - no banner item exists).
	// SourceLabelLower is cached for the @source token match.
	public readonly struct LootEntry
	{
		public readonly LootKind Kind;
		public readonly string SourceLabel;
		public readonly string SourceLabelLower;
		public readonly int SourceIconItem;
		public readonly int SourceHeadIndex;
		// > 0 when the source is an NPC (drop or shop). Lets BrowserSlotInteraction
		// spawn the NPC on Ctrl+click of the source icon. 0 for container / shimmer
		// rows where the source is itself an item.
		public readonly int SourceNpcType;
		public readonly int TargetItem;
		public readonly string Detail;
		public readonly string SearchText;
		public readonly string TargetNameLower;

		public LootEntry(LootKind kind, string sourceLabel, int sourceIconItem,
			int targetItem, string detail, string searchText,
			int sourceHeadIndex = 0, int sourceNpcType = 0)
		{
			Kind = kind;
			SourceLabel = sourceLabel;
			SourceLabelLower = sourceLabel.ToLowerInvariant();
			SourceIconItem = sourceIconItem;
			SourceHeadIndex = sourceHeadIndex;
			SourceNpcType = sourceNpcType;
			TargetItem = targetItem;
			Detail = detail;
			SearchText = searchText;
			TargetNameLower = ItemName(targetItem).ToLowerInvariant();
		}
	}

	private static List<LootEntry>? _entries;
	public static IReadOnlyList<LootEntry> All
	{
		get { EnsureBuilt(); return _entries!; }
	}

	private static void EnsureBuilt()
	{
		if (_entries is not null) return;
		var list = new List<LootEntry>(8192);
		try { CollectNpcDrops(list);       } catch (System.Exception e) { LogWarn("NPC drops",  e); }
		try { CollectItemKeyedDrops(list); } catch (System.Exception e) { LogWarn("Item drops", e); }
		try { CollectShops(list);          } catch (System.Exception e) { LogWarn("NPC shops",  e); }
		try { CollectShimmer(list);        } catch (System.Exception e) { LogWarn("Shimmer",    e); }
		_entries = list;
	}

	// Force a build off the hot path. Called from
	// GlobalRecipeBrowserSystem.OnWorldLoad so the first browser open
	// doesn't stutter on the ~8k-row scan.
	public static void Warm() => EnsureBuilt();

	private static void LogWarn(string section, System.Exception e)
	{
		ModLoader.GetMod("GregTechCEuTerraria")?.Logger.Warn(
			$"[loot-registry] {section} extraction failed: {e.GetType().Name}: {e.Message}");
	}

	// === NPC drops ====================================================
	// Walks every concrete (non-negative-alias) NPC type and asks each
	// rule chain to ReportDroprates - the same path the Bestiary uses
	// to populate its drop list. Skips negative net-IDs (those are
	// "global drop" aliases keyed by Main.ItemDropsDB; their entries
	// are surfaced as standalone "Universal Drops" rows below).
	private static void CollectNpcDrops(List<LootEntry> list)
	{
		var db = Main.ItemDropsDB;
		if (db is null) return;

		int npcCount = NPCLoader.NPCCount;
		var drops = new List<DropRateInfo>();
		for (int npcId = 1; npcId < npcCount; npcId++)
		{
			drops.Clear();
			var rules = db.GetRulesForNPCID(npcId, includeGlobalDrops: false);
			if (rules is null || rules.Count == 0) continue;

			var feed = new DropRateInfoChainFeed(1f);
			foreach (var rule in rules) rule.ReportDroprates(drops, feed);
			if (drops.Count == 0) continue;

			string npcName = Lang.GetNPCNameValue(npcId);
			if (string.IsNullOrWhiteSpace(npcName)) continue;
			int bannerSprite = Item.NPCtoBanner(npcId);
			int iconItem = bannerSprite > 0 ? Item.BannerToItem(bannerSprite) : 0;

			foreach (var d in drops)
			{
				if (d.itemId <= 0) continue;
				string detail = FormatDrop(d);
				list.Add(new LootEntry(
					kind: LootKind.NpcDrop,
					sourceLabel: npcName,
					sourceIconItem: iconItem,
					targetItem: d.itemId,
					detail: detail,
					searchText: BuildSearchText(npcName, d.itemId, detail),
					sourceNpcType: npcId));
			}
		}
	}

	private static string FormatDrop(DropRateInfo d)
	{
		var sb = new StringBuilder();
		// Stack range
		if (d.stackMin == d.stackMax)
		{
			if (d.stackMin > 1) sb.Append(d.stackMin).Append("x ");
		}
		else
		{
			sb.Append(d.stackMin).Append('-').Append(d.stackMax).Append("x ");
		}
		// Chance
		float pct = d.dropRate * 100f;
		if (pct >= 99.95f) sb.Append("100%");
		else if (pct >= 10f) sb.Append(pct.ToString("0.#")).Append('%');
		else if (pct >= 1f)  sb.Append(pct.ToString("0.##")).Append('%');
		else                 sb.Append(pct.ToString("0.###")).Append('%');
		// Conditions
		if (d.conditions is { Count: > 0 })
		{
			foreach (var c in d.conditions)
			{
				if (c is null) continue;
				string desc;
				try { desc = c.GetConditionDescription(); }
				catch { desc = ""; }
				if (string.IsNullOrWhiteSpace(desc)) continue;
				sb.Append("  *  ").Append(desc);
			}
		}
		return sb.ToString();
	}

	// === Item-keyed drops =============================================
	// Walks every item id and pulls drop rules registered to that ITEM
	// (Main.ItemDropsDB._entriesByItemId, exposed via the tML extension
	// `GetRulesForItemID`). This is the path crates, treasure bags,
	// lockboxes, biome keys, fishing crates, herb bags etc. use - they
	// drop loot when *opened* rather than when an NPC is killed, so they
	// don't surface through the NPC-keyed walk. Source icon is the
	// container item itself; source label is its display name.
	private static void CollectItemKeyedDrops(List<LootEntry> list)
	{
		var db = Main.ItemDropsDB;
		if (db is null) return;

		var drops = new List<DropRateInfo>();
		foreach (var kv in ContentSamples.ItemsByType)
		{
			int srcItemId = kv.Key;
			if (srcItemId <= 0) continue;
			var rules = db.GetRulesForItemID(srcItemId);
			if (rules is null || rules.Count == 0) continue;

			drops.Clear();
			var feed = new DropRateInfoChainFeed(1f);
			foreach (var rule in rules) rule.ReportDroprates(drops, feed);
			if (drops.Count == 0) continue;

			string srcName = kv.Value?.Name ?? string.Empty;
			if (string.IsNullOrWhiteSpace(srcName)) continue;

			foreach (var d in drops)
			{
				if (d.itemId <= 0) continue;
				string detail = FormatDrop(d);
				list.Add(new LootEntry(
					kind: LootKind.NpcDrop,
					sourceLabel: srcName,
					sourceIconItem: srcItemId,
					targetItem: d.itemId,
					detail: detail,
					searchText: BuildSearchText(srcName, d.itemId, detail)));
			}
		}
	}

	// === NPC shops ====================================================
	private static void CollectShops(List<LootEntry> list)
	{
		foreach (var shop in NPCShopDatabase.AllShops)
		{
			if (shop is null) continue;
			int npcType = shop.NpcType;
			if (npcType <= 0) continue;
			string npcName = Lang.GetNPCNameValue(npcType);
			if (string.IsNullOrWhiteSpace(npcName)) continue;
			string label = shop.Name == "Shop" ? npcName : $"{npcName} ({shop.Name})";
			// Town NPCs don't have banner items - use their head index so
			// UILootList can render the bestiary-style portrait instead.
			int headIdx = NPC.TypeToDefaultHeadIndex(npcType);

			foreach (var entry in shop.ActiveEntries)
			{
				if (entry?.Item is null || entry.Item.IsAir) continue;
				int itemId = entry.Item.type;
				string detail = FormatShopEntry(entry);
				list.Add(new LootEntry(
					kind: LootKind.Shop,
					sourceLabel: label,
					sourceIconItem: 0,
					targetItem: itemId,
					detail: detail,
					searchText: BuildSearchText(label, itemId, detail),
					sourceHeadIndex: headIdx,
					sourceNpcType: npcType));
			}
		}
	}

	private static string FormatShopEntry(AbstractNPCShop.Entry entry)
	{
		var sb = new StringBuilder();
		long price = entry.Item.value > 0 ? entry.Item.value : 0;
		if (price > 0) sb.Append(FormatCoinPrice(price));
		else sb.Append("Free");
		bool first = true;
		foreach (var cond in entry.Conditions)
		{
			if (cond is null) continue;
			string desc = cond.Description?.Value ?? "";
			if (string.IsNullOrWhiteSpace(desc)) continue;
			sb.Append(first ? "  *  " : ", ");
			sb.Append(desc);
			first = false;
		}
		return sb.ToString();
	}

	private static string FormatCoinPrice(long copper)
	{
		long platinum = copper / 1_000_000; copper -= platinum * 1_000_000;
		long gold     = copper / 10_000;    copper -= gold * 10_000;
		long silver   = copper / 100;       copper -= silver * 100;
		var sb = new StringBuilder();
		if (platinum > 0) sb.Append(platinum).Append("p ");
		if (gold > 0)     sb.Append(gold).Append("g ");
		if (silver > 0)   sb.Append(silver).Append("s ");
		if (copper > 0)   sb.Append(copper).Append("c");
		string s = sb.ToString().TrimEnd();
		return s.Length == 0 ? "0c" : s;
	}

	// === Shimmer ======================================================
	private static void CollectShimmer(List<LootEntry> list)
	{
		var table = ItemID.Sets.ShimmerTransformToItem;
		if (table is null) return;
		for (int srcId = 1; srcId < table.Length; srcId++)
		{
			int dstId = table[srcId];
			if (dstId <= 0 || dstId == srcId) continue;
			string srcName = ItemName(srcId);
			string dstName = ItemName(dstId);
			if (string.IsNullOrEmpty(srcName) || string.IsNullOrEmpty(dstName)) continue;
			string label = $"Shimmer - {srcName}";
			list.Add(new LootEntry(
				kind: LootKind.Shimmer,
				sourceLabel: label,
				sourceIconItem: srcId,
				targetItem: dstId,
				detail: "Toss into Shimmer",
				searchText: BuildSearchText(label, dstId, "shimmer transmutation")));
		}
	}

	private static string ItemName(int itemId)
	{
		if (itemId <= 0) return string.Empty;
		if (ContentSamples.ItemsByType.TryGetValue(itemId, out var sample))
			return sample.Name ?? string.Empty;
		return string.Empty;
	}

	private static string BuildSearchText(string sourceLabel, int targetItem, string detail)
	{
		var sb = new StringBuilder();
		sb.Append(sourceLabel.ToLowerInvariant()).Append(' ');
		string n = ItemName(targetItem);
		if (n.Length > 0) sb.Append(n.ToLowerInvariant()).Append(' ');
		if (!string.IsNullOrEmpty(detail)) sb.Append(detail.ToLowerInvariant());
		return sb.ToString();
	}

	// === Search =======================================================
	public static bool Matches(in LootEntry entry, string[] tokens)
	{
		if (tokens.Length == 0) return true;
		string text = entry.SearchText;
		string source = entry.SourceLabelLower;
		foreach (var t in tokens)
		{
			if (t.Length == 0) continue;

			// `@<text>` = source-only match. Mirrors RecipeSearch's `@station:`
			// semantics - type `@king` to scope to King Slime, `@merchant`
			// to scope to the Merchant's shop, etc.
			if (t[0] == '@')
			{
				string needle = t.Substring(1);
				if (needle.Length == 0) continue;           // bare '@' = no constraint
				if (!source.Contains(needle)) return false;
				continue;
			}

			if (!text.Contains(t)) return false;
		}
		return true;
	}

	// True if every non-`@` token matches the target item's name - the
	// "outputs first" rank the recipe-browser uses to sort matches that
	// produce the searched term ahead of matches that only reference it.
	// `@source` tokens are ignored here (already enforced by Matches and
	// don't discriminate target-vs-source).
	public static bool MatchesTarget(in LootEntry entry, string[] tokens)
	{
		if (tokens.Length == 0) return true;
		string name = entry.TargetNameLower;
		foreach (var t in tokens)
		{
			if (t.Length == 0 || t[0] == '@') continue;
			if (!name.Contains(t)) return false;
		}
		return true;
	}
}
