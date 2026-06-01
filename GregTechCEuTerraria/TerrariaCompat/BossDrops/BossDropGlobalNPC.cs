#nullable enable
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.BossDrops;

// Adds tier-appropriate GregTech drops onto vanilla bosses. The drop table is
// resolved once at Mod.Load (BossDropRegistry.Resolve); this hook just walks
// the resolved list and registers ItemDropWithConditionRules so the
// EnableBossDrops config flag can toggle drops at runtime.
//
// Multiblock bags ride alongside the material drops, gated by the same
// config flag but at a 1/100 per-eligible-boss rate (see BagDropChance).
// Bags are assigned to bosses via MultiblockBagTierMap -> the BossTable tier
// index that owns the matching tier - the same tier-map BossDropRegistry uses
// for materials, so a single tier change shifts both surfaces together.
public sealed class BossDropGlobalNPC : GlobalNPC
{
	private static readonly BossDropCondition Condition = new();
	private const int BagDropChance = 100;

	public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
	{
		if (BossDropRegistry.TryGet((short)npc.type, out var drops))
		{
			foreach (var d in drops)
				npcLoot.Add(new ItemDropWithConditionRule(d.ItemType, chanceDenominator: 1, amountDroppedMinimum: d.Min, amountDroppedMaximum: d.Max, Condition));
		}

		// Multiblock bags - one ItemDropWithConditionRule per bag whose target
		// tier matches this boss. Independent rule per bag so multiple bags
		// can roll on the same kill (rare, but possible with enough bags at a
		// tier).
		if (BossDropRegistry.TryGetBags((short)npc.type, out var bags))
		{
			foreach (var bagItemType in bags)
				npcLoot.Add(new ItemDropWithConditionRule(bagItemType, chanceDenominator: BagDropChance, amountDroppedMinimum: 1, amountDroppedMaximum: 1, Condition));
		}
	}
}
