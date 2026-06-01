#nullable enable
using System.Text.Json;
using GregTechCEuTerraria.Api.Recipe;
using Terraria.ID;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - JSON dispatch hub for upstream's recipe-condition schema.
//
// Reads upstream's RecipeCondition JSON form into the matching concrete
// subclass:
//
//   {"type": "gtceu:raining",          "level": 0.5}
//   {"type": "gtceu:thunder",          "level": 0.0}
//   {"type": "gtceu:daytime",          "daytime": 1}
//   {"type": "gtceu:dimension",        "dimension": "minecraft:overworld"}
//   {"type": "gtceu:biome",            "biome": "minecraft:plains"}
//   {"type": "gtceu:biome_tag",        "tag": "minecraft:is_hot"}
//   {"type": "gtceu:pos_y",            "min": 0, "max": 64}
//   {"type": "gtceu:cleanroom",        "cleanroom": "clean_room"}
//   {"type": "gtceu:eu_to_start",      "eu": 100000}
//   {"type": "gtceu:research",         "research": "...", "data_stack": {...}}
//   {"type": "gtceu:adjacent_block",   "block": "modid:block", "min": 2}
//   {"type": "gtceu:adjacent_fluid",   "fluid": "minecraft:water", "min": 1}
//   {"type": "gtceu:vent"}
//   {"type": "gtceu:environmental_hazard", "hazard_type": "radiation"}
//
// Third-party mod conditions (FTBQuestCondition, GameStageCondition,
// HeraclesQuestCondition) dropped entirely - they integrate with mods we
// don't have.
//
// Each condition has an `"isReverse": true` flag in the JSON that inverts
// the predicate (matches upstream's RecipeCondition.isReverse). We honor
// it via SetReverse(true) on the constructed condition.
public static class RecipeConditionJson
{
	public static RecipeCondition? Read(JsonElement el)
	{
		if (el.ValueKind != JsonValueKind.Object) return null;
		if (!el.TryGetProperty("type", out var typeEl)) return null;
		string type = typeEl.GetString() ?? "";
		type = StripNs(type);

		RecipeCondition? cond = type switch
		{
			"raining"               => new RainingCondition(GetFloat(el, "level", 0f)),
			"thunder"               => new ThunderCondition(GetFloat(el, "level", 0f)),
			"daytime"               => new DaytimeCondition(GetInt(el, "daytime", 1)),
			"dimension"             => new DimensionCondition(GetString(el, "dimension", "")),
			"biome"                 => new BiomeCondition(GetString(el, "biome", "")),
			"biome_tag"             => new BiomeTagCondition(GetString(el, "tag", "")),
			"pos_y"                 => new PositionYCondition(GetInt(el, "min", int.MinValue), GetInt(el, "max", int.MaxValue)),
			// Bundled recipes ship the `"cleanroom"` field populated; the
			// default is for hand-authored recipes that omit it. Matches
			// upstream `CleanroomCondition` initializer default (CLEANROOM).
			"cleanroom"             => new CleanroomCondition(GetString(el, "cleanroom", "cleanroom")),
			"eu_to_start"           => new EUToStartCondition(GetLong(el, "eu", 0L)),
			"research"              => new ResearchCondition(ParseResearchId(el)),
			"adjacent_block"        => new AdjacentBlockCondition(
				                            (ushort)0,  // tile-type resolution deferred to a later JSON-mapping wave
				                            GetInt(el, "min", 1)),
			"adjacent_fluid"        => new AdjacentFluidCondition(
				                            ResolveLiquidId(GetString(el, "fluid", "minecraft:water")),
				                            GetInt(el, "min", 1)),
			"vent"                  => new VentCondition(),
			"environmental_hazard"  => new EnvironmentalHazardCondition(GetString(el, "condition", "")),
			// Third-party / unknown - drop silently. Upstream throws here;
			// we degrade to null so unknown conditions don't break recipe load.
			_ => null,
		};

		if (cond is null) return null;

		// Honor reverse flag.
		if (el.TryGetProperty("isReverse", out var reverseEl) && reverseEl.GetBoolean())
			cond.SetReverse(true);

		return cond;
	}

	private static string StripNs(string s)
	{
		int idx = s.IndexOf(':');
		return idx < 0 ? s : s[(idx + 1)..];
	}

	private static short ResolveLiquidId(string upstreamFluidId) => upstreamFluidId switch
	{
		"minecraft:water" => LiquidID.Water,
		"minecraft:lava"  => LiquidID.Lava,
		"forge:honey"     => LiquidID.Honey,
		_                 => LiquidID.Water,
	};

	private static int    GetInt(JsonElement el, string key, int def) =>
		el.TryGetProperty(key, out var v) ? v.GetInt32() : def;
	private static long   GetLong(JsonElement el, string key, long def) =>
		el.TryGetProperty(key, out var v) ? v.GetInt64() : def;
	private static float  GetFloat(JsonElement el, string key, float def) =>
		el.TryGetProperty(key, out var v) ? v.GetSingle() : def;
	private static string GetString(JsonElement el, string key, string def) =>
		el.TryGetProperty(key, out var v) ? (v.GetString() ?? def) : def;

	// Upstream's research-condition JSON shape is an array of entries, each
	// with `{ dataItem: { tag: { assembly_line_research: { research_id: "..." }}}}`.
	// We only need the research_id of the first entry - that's the recipe's
	// research key (matched against `IDataAccessHatch`'s recipe set).
	private static string ParseResearchId(JsonElement el)
	{
		if (!el.TryGetProperty("research", out var research)) return "";
		if (research.ValueKind == System.Text.Json.JsonValueKind.String)
			return research.GetString() ?? "";   // legacy single-string shape (safety)
		if (research.ValueKind != System.Text.Json.JsonValueKind.Array) return "";
		foreach (var entry in research.EnumerateArray())
		{
			if (!entry.TryGetProperty("dataItem", out var data)) continue;
			if (!data.TryGetProperty("tag", out var tag)) continue;
			if (!tag.TryGetProperty("assembly_line_research", out var alr)) continue;
			if (!alr.TryGetProperty("research_id", out var idEl)) continue;
			return idEl.GetString() ?? "";
		}
		return "";
	}
}
