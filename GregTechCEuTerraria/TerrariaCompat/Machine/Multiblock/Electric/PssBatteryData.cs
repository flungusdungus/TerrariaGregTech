#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine.Multiblock;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Verbatim port of upstream BatteryBlock.BatteryPartType (9 enum entries).
// Battery blocks auto-register as CasingTiles via CasingRegistry; this map
// keys casing tile id -> IBatteryData for PowerSubstationMachine's pattern
// factory. Empty variants (tier=-1) allowed but contribute no capacity
// (upstream-verbatim filler-slot behavior).
public static class PssBatteryData
{
	private sealed record Entry(int Tier, long Capacity, string BatteryName) : IBatteryData
	{
		int    IBatteryData.Tier        => Tier;
		long   IBatteryData.Capacity    => Capacity;
		string IBatteryData.BatteryName => BatteryName;
	}

	// Capacities verbatim from BatteryBlock.BatteryPartType.
	private static readonly Dictionary<string, IBatteryData> _byTileName = new()
	{
		// Tier-I (EV / IV)
		["empty_tier_i_battery"]   = new Entry(-1,            0,                          "empty_tier_i"),
		["ev_lapotronic_battery"]  = new Entry( 4,   25_000_000L * 6,                   "ev_lapotronic"),
		["iv_lapotronic_battery"]  = new Entry( 5,  250_000_000L * 6,                   "iv_lapotronic"),
		// Tier-II (LuV / ZPM)
		["empty_tier_ii_battery"]  = new Entry(-1,            0,                          "empty_tier_ii"),
		["luv_lapotronic_battery"] = new Entry( 6, 1_000_000_000L * 6,                  "luv_lapotronic"),
		["zpm_lapotronic_battery"] = new Entry( 7, 4_000_000_000L * 6,                  "zpm_lapotronic"),
		// Tier-III (UV / UHV) - UHV is sentinel
		["empty_tier_iii_battery"] = new Entry(-1,            0,                          "empty_tier_iii"),
		["uv_lapotronic_battery"]  = new Entry( 8, 16_000_000_000L * 6,                 "uv_lapotronic"),
		["uhv_ultimate_battery"]   = new Entry( 9, long.MaxValue,                       "uhv_ultimate"),
	};

	public static IReadOnlyDictionary<string, IBatteryData> All => _byTileName;

	public static IBatteryData? Get(string tileName) =>
		_byTileName.TryGetValue(tileName, out var d) ? d : null;
}
