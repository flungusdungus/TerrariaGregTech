#nullable enable
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Recipe;

// Type-tolerant reads for recipe `data` values.
//
// Our JSON serializer (GTRecipeSerializer.ReadTag) stores JSON numbers as Int32
// when they fit, so a JSON `"duration_is_total_cwu": 1` lands as an int. tML's
// TagCompound.GetBool is strict - it casts the stored object straight to byte
// and throws `InvalidCastException: Int32 -> Byte` / an NBT IOException. Upstream
// stores these in NBT where `getBoolean` coerces from any NumericTag, so the
// verbatim `recipe.data.getBoolean(key)` port NREs/throws at runtime on every
// recipe that carries an int-valued boolean flag (e.g. every research_station
// recipe's `duration_is_total_cwu`). This mirrors the Common-side
// GTRecipeModifiers.ReadDataLong / ReadDataInt fix for the bool case.
public static class RecipeDataUtil
{
	public static bool GetBool(TagCompound? data, string key)
	{
		if (data == null || !data.ContainsKey(key)) return false;
		return data[key] switch
		{
			bool b   => b,
			byte by  => by != 0,
			sbyte sb => sb != 0,
			short s  => s != 0,
			int i    => i != 0,
			long l   => l != 0,
			_        => false,
		};
	}
}
