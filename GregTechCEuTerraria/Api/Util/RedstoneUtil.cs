#nullable enable
using System;
using System.Numerics;

namespace GregTechCEuTerraria.Api.Util;

// Port of com.gregtechceu.gtceu.utils.RedstoneUtil.
//
// Upstream's package is `com.gregtechceu.gtceu.utils`; we have no `utils`
// mirror folder, so it sits under Api/Util with the other gtceu util ports.
//
// Computes a 0-15 analog redstone strength from a stored/capacity ratio. In
// the Terraria port the consumers (detector covers) collapse the result to a
// binary on/off (`value > 0`) - Terraria wire has no analog level. The math
// here is a verbatim port so a side-by-side audit against RedstoneUtil.java
// still holds; the binary collapse happens once, in DetectorCover.
//
// Upstream's BigInteger overload of computeRedstoneBetweenValues is omitted -
// no detector calls it (and it has a known min/max compare typo upstream).
public static class RedstoneUtil
{
	// computeRedstoneValue(long, long, boolean)
	public static int ComputeRedstoneValue(long current, long max, bool isInverted)
	{
		int output = (int)(14f * current / max) + (current > 0 ? 1 : 0);
		return isInverted ? 15 - output : output;
	}

	// computeRedstoneValue(BigInteger, BigInteger, boolean)
	public static int ComputeRedstoneValue(BigInteger current, BigInteger max, bool isInverted)
	{
		bool isNotEmpty = current > BigInteger.Zero;
		int output = (int)(14f * Ratio(current, max)) + (isNotEmpty ? 1 : 0);
		return isInverted ? 15 - output : output;
	}

	// computeRedstoneBetweenValues(float, float, float, boolean)
	public static int ComputeRedstoneBetweenValues(float value, float maxValue, float minValue, bool isInverted)
	{
		if (value >= maxValue) return isInverted ? 0 : 15;
		if (value <= minValue) return isInverted ? 15 : 0;

		float ratio = isInverted
			? 15 * (maxValue - value) / (maxValue - minValue)
			: 15 * (value - minValue) / (maxValue - minValue);
		// Java Math.round = floor(x + 0.5).
		return (int)MathF.Floor(ratio + 0.5f);
	}

	// computeLatchedRedstoneBetweenValues(float, float, float, boolean, int)
	public static int ComputeLatchedRedstoneBetweenValues(float value, float maxValue, float minValue,
		bool isInverted, int output)
	{
		if (value >= maxValue) output = isInverted ? 15 : 0;
		else if (value <= minValue) output = isInverted ? 0 : 15;
		return output;
	}

	// computeLatchedRedstoneBetweenValues(BigInteger, BigInteger, BigInteger, boolean, int)
	public static int ComputeLatchedRedstoneBetweenValues(BigInteger value, BigInteger maxValue, BigInteger minValue,
		bool isInverted, int output)
	{
		if (value >= maxValue) output = isInverted ? 15 : 0;
		else if (value <= minValue) output = isInverted ? 0 : 15;
		return output;
	}

	// Port of GTMath.ratio(BigInteger, BigInteger) - a float quotient.
	public static float Ratio(BigInteger num, BigInteger den) =>
		den.IsZero ? 0f : (float)((double)num / (double)den);
}
