#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace GregTechCEuTerraria.Api.Tool;

// Port of upstream com.gregtechceu.gtceu.api.item.tool.IGTToolDefinition +
// ToolDefinitionBuilder, collapsed into one class. Upstream splits an
// interface, an anonymous implementation built by ToolDefinitionBuilder, and
// the builder itself; the interface's stat getters all take an ItemStack the
// anonymous class ignores, so per-stack variation never happens - the data is
// a flat immutable record.
//
// Documented deviations from upstream:
//   - IToolBehavior objects -> a flat list of behavior name strings. The
//     Terraria ToolItem branches on these by name (only torch_place /
//     tree_felling / aoe carry over; the rest have no Terraria mechanic).
//   - effectiveBlocks / effectiveStates / isToolEffective -> dropped (MC Block
//     predicates; Terraria harvest is power-based, not predicate-based).
//   - enchantment members (isEnchantable / canApplyEnchantment /
//     defaultEnchantments) -> dropped; Terraria has no enchantment system.
//   - brokenStack Supplier<ItemStack> -> BrokenStackId string (the power_unit
//     item an electric tool leaves behind), resolved by the loader.
public sealed class ToolDefinition
{
	// Sentinel: cannotAttack() sets BaseDamage to this. A tool with this base
	// damage always reports 0 total attack damage (mirrors Float.MIN_VALUE
	// upstream). See IGTTool.getTotalAttackDamage.
	public const float NoAttackSentinel = float.Epsilon;

	public IReadOnlyList<string> Behaviors { get; init; } = new List<string>();
	public int DamagePerAction { get; init; } = 1;
	public int DamagePerCraftingAction { get; init; } = 1;
	public bool SuitableForBlockBreaking { get; init; }
	public bool SuitableForAttacking { get; init; }
	public bool SuitableForCrafting { get; init; }
	public int BaseDurability { get; init; }
	public float DurabilityMultiplier { get; init; } = 1.0f;
	public int BaseQuality { get; init; }
	public float BaseDamage { get; init; }
	public float BaseEfficiency { get; init; } = 4f;
	public float EfficiencyMultiplier { get; init; } = 1.0f;
	public float AttackSpeed { get; init; }
	public bool SneakBypassUse { get; init; }
	public AoESymmetrical Aoe { get; init; } = AoESymmetrical.ZERO;
	public string? BrokenStackId { get; init; }

	// Verbatim ports of the IGTToolDefinition default durability-per-X methods.
	// A tool unsuited to an action takes double durability damage doing it.
	public int GetToolDamagePerBlockBreak() =>
		SuitableForBlockBreaking ? DamagePerAction : DamagePerAction * 2;

	public int GetToolDamagePerAttack() =>
		SuitableForAttacking ? DamagePerAction : DamagePerAction * 2;

	public int GetToolDamagePerCraft() =>
		SuitableForCrafting ? DamagePerCraftingAction : DamagePerCraftingAction * 2;

	public bool HasBehavior(string name) => Behaviors.Contains(name);

	// Fluent builder - verbatim mirror of upstream ToolDefinitionBuilder so the
	// GTToolType registry's `.toolStats(b -> ...)` lambdas read 1:1.
	public sealed class Builder
	{
		private readonly List<string> _behaviors = new();
		private int _damagePerAction = 1;
		private int _damagePerCraftingAction = 1;
		private bool _suitableForBlockBreaking;
		private bool _suitableForAttacking;
		private bool _suitableForCrafting;
		private int _baseDurability;
		private float _durabilityMultiplier = 1.0f;
		private int _baseQuality;
		private float _attackDamage;
		private float _baseEfficiency = 4f;
		private float _efficiencyMultiplier = 1.0f;
		private float _attackSpeed;
		private bool _sneakBypassUse;
		private AoESymmetrical _aoe = AoESymmetrical.ZERO;
		private string? _brokenStackId;

		public Builder Behaviors(params string[] behaviors)
		{
			_behaviors.AddRange(behaviors);
			return this;
		}

		public Builder BlockBreaking()
		{
			_suitableForBlockBreaking = true;
			return this;
		}

		public Builder Attacking()
		{
			_suitableForAttacking = true;
			return this;
		}

		public Builder Crafting()
		{
			_suitableForCrafting = true;
			return this;
		}

		public Builder DamagePerAction(int value)
		{
			_damagePerAction = value;
			return this;
		}

		public Builder DamagePerCraftingAction(int value)
		{
			_damagePerCraftingAction = value;
			return this;
		}

		public Builder BaseDurability(int value)
		{
			_baseDurability = value;
			return this;
		}

		public Builder DurabilityMultiplier(float value)
		{
			_durabilityMultiplier = value;
			return this;
		}

		public Builder BaseQuality(int value)
		{
			_baseQuality = value;
			return this;
		}

		public Builder AttackDamage(float value)
		{
			_attackDamage = value;
			return this;
		}

		// Mirrors ToolDefinitionBuilder.cannotAttack - forces total attack
		// damage to 0 regardless of material stats.
		public Builder CannotAttack()
		{
			_attackDamage = NoAttackSentinel;
			return this;
		}

		public Builder BaseEfficiency(float value)
		{
			_baseEfficiency = value;
			return this;
		}

		public Builder EfficiencyMultiplier(float value)
		{
			_efficiencyMultiplier = value;
			return this;
		}

		public Builder AttackSpeed(float value)
		{
			_attackSpeed = value;
			return this;
		}

		public Builder SneakBypassUse()
		{
			_sneakBypassUse = true;
			return this;
		}

		public Builder Aoe(int additionalColumns, int additionalRows, int additionalDepth)
		{
			_aoe = AoESymmetrical.Of(additionalColumns, additionalRows, additionalDepth);
			return this;
		}

		public Builder BrokenStack(string powerUnitId)
		{
			_brokenStackId = powerUnitId;
			return this;
		}

		public ToolDefinition Build() => new()
		{
			Behaviors = _behaviors,
			DamagePerAction = _damagePerAction,
			DamagePerCraftingAction = _damagePerCraftingAction,
			SuitableForBlockBreaking = _suitableForBlockBreaking,
			SuitableForAttacking = _suitableForAttacking,
			SuitableForCrafting = _suitableForCrafting,
			BaseDurability = _baseDurability,
			DurabilityMultiplier = _durabilityMultiplier,
			BaseQuality = _baseQuality,
			BaseDamage = _attackDamage,
			BaseEfficiency = _baseEfficiency,
			EfficiencyMultiplier = _efficiencyMultiplier,
			AttackSpeed = _attackSpeed,
			SneakBypassUse = _sneakBypassUse,
			Aoe = _aoe,
			BrokenStackId = _brokenStackId,
		};
	}
}
