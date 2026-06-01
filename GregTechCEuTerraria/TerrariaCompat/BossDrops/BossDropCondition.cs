#nullable enable
using GregTechCEuTerraria.Config;
using Terraria.GameContent.ItemDropRules;
using Terraria.Localization;

namespace GregTechCEuTerraria.TerrariaCompat.BossDrops;

// Gates a CommonDrop on the EnableBossDrops config flag - re-evaluated per
// drop attempt, so a runtime config toggle takes effect on the next boss kill
// without a world reload.
public sealed class BossDropCondition : IItemDropRuleCondition
{
	public bool CanDrop(DropAttemptInfo info) => GTConfig.Instance?.EnableBossDrops ?? true;
	public bool CanShowItemDropInUI() => GTConfig.Instance?.EnableBossDrops ?? true;
	public string GetConditionDescription() => Language.GetTextValue("Mods.GregTechCEuTerraria.BossDrops.ConditionDescription");
}
