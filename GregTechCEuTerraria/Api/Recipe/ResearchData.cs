#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Recipe;

// Port of com.gregtechceu.gtceu.api.recipe.ResearchData.
//
// A list of `(researchId, dataItem)` entries - assembly_line recipes
// declare these so an Assembly Line / Data Bank can issue research and
// later look up which recipes a given data stick unlocks.
//
// Documented adaptations:
//   - Mojang `Codec<T>` / `RecordCodecBuilder` DROPPED - JSON round-trip
//     goes through Newtonsoft.Json directly (matches the rest of our
//     recipe loader). Network round-trip uses `BinaryReader/Writer` via
//     `TagCompound` - only used if a server pushes research data to
//     clients (we don't yet, so this surface is dormant).
//   - `ItemStack` -> `Terraria.Item` (stack quantity preserved as int).
//   - `FriendlyByteBuf` -> TagCompound for cross-network serialization.
//
// Preserved verbatim:
//   - `add(ResearchEntry)`, `iterator()` (= IEnumerable<ResearchEntry>).
//   - Nested `ResearchEntry(researchId, dataItem)` record.
public sealed class ResearchData : IEnumerable<ResearchData.ResearchEntry>
{
	private readonly List<ResearchEntry> _entries;

	public ResearchData()                           { _entries = new(); }
	public ResearchData(List<ResearchEntry> entries) { _entries = entries; }

	public void Add(ResearchEntry entry) => _entries.Add(entry);

	public IEnumerator<ResearchEntry> GetEnumerator() => _entries.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public static ResearchData FromJson(JsonElement array)
	{
		var entries = new List<ResearchEntry>();
		if (array.ValueKind == JsonValueKind.Array)
		{
			foreach (var element in array.EnumerateArray())
				entries.Add(ResearchEntry.FromJson(element));
		}
		return new ResearchData(entries);
	}

	public TagCompound ToNbt()
	{
		var list = new List<TagCompound>(_entries.Count);
		foreach (var entry in _entries) list.Add(entry.ToNbt());
		return new TagCompound { ["entries"] = list };
	}

	public static ResearchData FromNbt(TagCompound tag)
	{
		var entries = new List<ResearchEntry>();
		if (tag.ContainsKey("entries"))
		{
			foreach (var sub in tag.GetList<TagCompound>("entries"))
				entries.Add(ResearchEntry.FromNbt(sub));
		}
		return new ResearchData(entries);
	}

	// An entry containing information about a researchable recipe.
	// Used for internal research storage and JEI integration upstream;
	// for us it's pure data carried alongside an assembly_line recipe.
	public sealed class ResearchEntry
	{
		public string ResearchId { get; }
		public Item   DataItem   { get; }

		public ResearchEntry(string researchId, Item dataItem)
		{
			ResearchId = researchId;
			DataItem   = dataItem;
		}

		public static ResearchEntry FromJson(JsonElement obj)
		{
			string researchId = obj.TryGetProperty("researchId", out var rid)
				? (rid.GetString() ?? "") : "";
			Item item = obj.TryGetProperty("dataItem", out var dataEl)
				? ItemFromJson(dataEl) : new Item();
			return new ResearchEntry(researchId, item);
		}

		public TagCompound ToNbt()
		{
			var tag = new TagCompound { ["researchId"] = ResearchId };
			if (DataItem != null && !DataItem.IsAir)
				tag["dataItem"] = ItemIO.Save(DataItem);
			return tag;
		}

		public static ResearchEntry FromNbt(TagCompound tag)
		{
			string id = tag.GetString("researchId");
			Item item = tag.ContainsKey("dataItem") ? ItemIO.Load(tag.GetCompound("dataItem")) : new Item();
			return new ResearchEntry(id, item);
		}

		// Compact JSON form for the data item - `{ id: <type>, count: <int> }`.
		// Full-NBT round-trip uses the TagCompound path instead.
		private static Item ItemFromJson(JsonElement token)
		{
			var item = new Item();
			if (token.ValueKind == JsonValueKind.Object)
			{
				int type  = token.TryGetProperty("id",    out var idEl) ? idEl.GetInt32() : 0;
				int count = token.TryGetProperty("count", out var ctEl) ? ctEl.GetInt32() : 1;
				if (type > 0) { item.SetDefaults(type); item.stack = count; }
			}
			return item;
		}
	}
}
