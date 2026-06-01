#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using Terraria;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.common.recipe.condition.DaytimeCondition.
//
// Recipe runs only at the specified time of day. Upstream encodes a
// "daytime" int matching MC's level.getDayTime() - we map to
// `Main.dayTime` (true = day) + Main.time for time-of-day comparison.
//
// Upstream supports any specific day-time int; for Terraria we coarse-grain
// to day-vs-night (1 = day, 0 = night) since fine-grained time-of-day gating
// is rarely meaningful in Terraria's day cycle.
public sealed class DaytimeCondition : RecipeCondition
{
	// 1 = day, 0 = night. Upstream uses a more granular value; the
	// adaptation collapses to a binary discriminator.
	public int Daytime { get; }

	public DaytimeCondition() : this(1) { }
	public DaytimeCondition(int daytime) { Daytime = daytime; }

	public override bool Test(RecipeLogic logic)
	{
		bool requireDay = Daytime != 0;
		return Main.dayTime == requireDay;
	}

	public override string GetTooltips() => Daytime != 0 ? "Requires daytime" : "Requires night";
	public override string GetTypeName() => "gtceu:daytime";
}
