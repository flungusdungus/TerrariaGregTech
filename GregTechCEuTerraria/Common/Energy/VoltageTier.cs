#nullable enable
using System.Collections.Generic;
using System.Numerics;

namespace GregTechCEuTerraria.Common.Energy;

// Mirrors upstream GTValues.V / VN / VOLTAGE_NAMES at tier index N.
// 15 voltage tiers from ULV (8 EU) to MAX (2,147,483,648 EU). Each tier is
// exactly 4x the previous. Used by IEnergyContainer, cables, machines, and
// recipes to validate compatibility and size packets.
public enum VoltageTier
{
	ULV = 0,
	LV  = 1,
	MV  = 2,
	HV  = 3,
	EV  = 4,
	IV  = 5,
	LuV = 6,
	ZPM = 7,
	UV  = 8,
	UHV = 9,
	UEV = 10,
	UIV = 11,
	UXV = 12,
	OpV = 13,
	MAX = 14,
}

public sealed record VoltageTierInfo(VoltageTier Tier, string ShortName, string LongName, long Voltage);

public static class VoltageTiers
{
	// Upstream GTValues.V[] - the 15 NAMED tier voltages (ULV..MAX).
	private static readonly long[] _voltages =
	{
		8L,           32L,           128L,          512L,
		2048L,        8192L,         32768L,        131072L,
		524288L,      2097152L,      8388608L,      33554432L,
		134217728L,   536870912L,    2147483648L,
	};

	// Upstream GTValues.VEX[] - the EXTENDED voltage table, 31 entries.
	// Indices 0..14 mirror V[] (named tiers ULV..MAX); indices 15..30 extend
	// the x4-per-step ladder up to Long.MAX_VALUE for overclock math that
	// produces voltages above MAX. Consumers that snap a raw voltage to its
	// on-tier value (`getOverclockVoltage`, `getMaxVoltage` in
	// WorkableElectricMultiblockMachine) MUST use VEX rather than V - V would
	// throw IndexOutOfRange once `FloorTierByVoltage` returns > MAX.
	private static readonly long[] _voltagesEx =
	{
		8L,           32L,           128L,          512L,
		2048L,        8192L,         32768L,        131072L,
		524288L,      2097152L,      8388608L,      33554432L,
		134217728L,   536870912L,    2147483648L,
		8589934592L,  34359738368L,  137438953472L, 549755813888L,
		2199023255552L, 8796093022208L, 35184372088832L, 140737488355328L,
		562949953421312L, 2251799813685248L, 9007199254740992L,
		36028797018963968L, 144115188075855872L, 576460752303423488L,
		2305843009213693952L, long.MaxValue,
	};

	// Upstream GTValues.VA[] - per-tier "voltage amperage", the EU/t draw of
	// active 1A machines (subtle bleed below `V[tier]` reflecting the running
	// overhead). Used by cleanroom EU drain and any other "1A draw per tick"
	// consumer that needs the upstream-canonical number rather than `V[tier]`.
	private static readonly int[] _voltageAmperage =
	{
		7,         30,         120,         480,
		1920,      7680,       30720,       122880,
		491520,    1966080,    7864320,     31457280,
		125829120, 503316480,  2013265920,
	};

	// Upstream GTValues.VCM (main text color per tier, mapped from MC's
	// ChatFormatting hex values). Used to tint tier-templated item names so
	// rarity is visible at a glance - MV cyan, HV gold, IV blue, UV teal, etc.
	private static readonly uint[] _textColors =
	{
		0x555555, // ULV - DARK_GRAY
		0xAAAAAA, // LV  - GRAY
		0x55FFFF, // MV  - AQUA
		0xFFAA00, // HV  - GOLD
		0xAA00AA, // EV  - DARK_PURPLE
		0x5555FF, // IV  - BLUE
		0xFF55FF, // LuV - LIGHT_PURPLE
		0xFF5555, // ZPM - RED
		0x00AAAA, // UV  - DARK_AQUA
		0xAA0000, // UHV - DARK_RED
		0x55FF55, // UEV - GREEN
		0x00AA00, // UIV - DARK_GREEN
		0xFFFF55, // UXV - YELLOW
		0x5555FF, // OpV - BLUE
		0xFF5555, // MAX - RED
	};

	// Upstream GTValues.VN
	private static readonly string[] _shortNames =
	{
		"ULV", "LV", "MV", "HV",
		"EV",  "IV", "LuV","ZPM",
		"UV",  "UHV","UEV","UIV",
		"UXV", "OpV","MAX",
	};

	// Upstream GTValues.VOLTAGE_NAMES
	private static readonly string[] _longNames =
	{
		"Ultra Low Voltage", "Low Voltage", "Medium Voltage", "High Voltage",
		"Extreme Voltage", "Insane Voltage", "Ludicrous Voltage", "ZPM Voltage",
		"Ultimate Voltage", "Ultra High Voltage", "Ultra Excessive Voltage", "Ultra Immense Voltage",
		"Ultra Extreme Voltage", "Overpowered Voltage", "Maximum Voltage",
	};

	public const int Count = 15;

	// Upstream GTValues.MAX_TRUE - the topmost VEX index (long.MaxValue).
	// Used by `FloorTierByVoltage` to special-case the saturation tier.
	public const int MAX_TRUE = 30;

	public static long Voltage(VoltageTier tier) => _voltages[(int)tier];

	// Indexed access to V[] / VEX[] by raw int tier - for callers that
	// receive a tier from `FloorTierByVoltage` (which can return values up to
	// MAX_TRUE) and need the on-tier voltage. V is bounds-checked at MAX (14);
	// VoltageEx covers the full 0..MAX_TRUE range.
	public static long V(int tier) => _voltages[tier];
	public static long VoltageEx(int tier) => _voltagesEx[tier];
	// Per-tier active 1A draw in EU/t (upstream GTValues.VA[tier]).
	public static int  VA(int tier) => _voltageAmperage[tier];
	public static string ShortName(VoltageTier tier) => _shortNames[(int)tier];

	// Lowercase short name for upstream id construction (e.g. "lv_macerator",
	// "iv_battery"). Matches upstream Java enum lower-cased.
	public static string Id(VoltageTier tier) => _shortNames[(int)tier].ToLowerInvariant();
	public static string LongName(VoltageTier tier) => _longNames[(int)tier];

	// XNA Color of this tier's main text color (upstream VCM[tier]). Suitable
	// for TooltipLine.OverrideColor or Terraria.Utils.DrawBorderString.
	public static Microsoft.Xna.Framework.Color TextColor(VoltageTier tier)
	{
		uint c = _textColors[(int)tier];
		return new Microsoft.Xna.Framework.Color((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
	}

	// Working-light tint for a tier - the per-tier color of `TextColor` but
	// normalized so the strongest channel sits at 1.0, i.e. vanilla-torch
	// brightness for whichever channel dominates that tier. Dim tiers
	// (ULV 0x555555) get pushed up to a visible white; saturated tiers
	// (MV cyan, HV gold, EV purple) keep their hue with full intensity. The
	// machine / lamp layer then multiplies this by its own brightness factor
	// (1.0 for generic machines, 1.0..3.5 for lamps).
	public static Microsoft.Xna.Framework.Vector3 LightColor(VoltageTier tier)
	{
		uint c = _textColors[(int)tier];
		float r = ((c >> 16) & 0xFF) / 255f;
		float g = ((c >>  8) & 0xFF) / 255f;
		float b = ( c        & 0xFF) / 255f;
		float max = System.Math.Max(r, System.Math.Max(g, b));
		if (max <= 0f) return new Microsoft.Xna.Framework.Vector3(1f, 1f, 1f);
		float k = 1f / max;
		return new Microsoft.Xna.Framework.Vector3(r * k, g * k, b * k);
	}

	public static VoltageTierInfo Info(VoltageTier tier) =>
		new(tier, _shortNames[(int)tier], _longNames[(int)tier], _voltages[(int)tier]);

	public static IEnumerable<VoltageTierInfo> All
	{
		get
		{
			for (int i = 0; i < Count; i++)
				yield return Info((VoltageTier)i);
		}
	}

	// Returns the lowest tier whose voltage >= requested. Useful for sizing a
	// cable / machine from a recipe's EU/t requirement.
	public static VoltageTier MinTierForVoltage(long voltage)
	{
		if (voltage <= 0) return VoltageTier.ULV;
		for (int i = 0; i < Count; i++)
			if (_voltages[i] >= voltage) return (VoltageTier)i;
		return VoltageTier.MAX;
	}

	// Returns the highest tier whose voltage <= requested. Useful for capping
	// a generator's output to what its tier supports.
	public static VoltageTier MaxTierForVoltage(long voltage)
	{
		var result = VoltageTier.ULV;
		for (int i = 0; i < Count; i++)
		{
			if (_voltages[i] > voltage) break;
			result = (VoltageTier)i;
		}
		return result;
	}

	// Verbatim port of GTUtil.getOCTierByVoltage - lowest tier index whose
	// voltage can handle `voltage` (>= voltage). Bit-twiddling identical to
	// upstream (Long.numberOfLeadingZeros -> BitOperations.LeadingZeroCount).
	public static int OcTierByVoltage(long voltage)
	{
		if (voltage <= _voltages[0]) return (int)VoltageTier.ULV;
		return (62 - BitOperations.LeadingZeroCount((ulong)(voltage - 1))) >> 1;
	}

	// Verbatim port of GTUtil.getTierByVoltage.
	public static int TierByVoltage(long voltage)
	{
		if (voltage > int.MaxValue) return (int)VoltageTier.MAX;
		return OcTierByVoltage(voltage);
	}

	// Verbatim port of GTUtil.getFloorTierByVoltage - the highest tier whose
	// voltage <= requested. Returns a value in [ULV, MAX_TRUE] (i.e. 0..30);
	// callers that need the on-tier voltage MUST use `VoltageEx(returnedTier)`
	// rather than `V(returnedTier)` since the returned tier can exceed MAX (14).
	public static int FloorTierByVoltage(long voltage)
	{
		if (voltage < _voltages[(int)VoltageTier.LV]) return (int)VoltageTier.ULV;
		// Exact saturation - long.MaxValue maps to MAX_TRUE rather than the
		// bit-twiddle's value (29 for Long.MAX_VALUE, since its top bit is 0).
		if (voltage == _voltagesEx[MAX_TRUE]) return MAX_TRUE;
		return (60 - BitOperations.LeadingZeroCount((ulong)voltage)) >> 1;
	}
}
