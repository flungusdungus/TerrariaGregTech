#nullable enable
using System;
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Tool;

// Port of upstream com.gregtechceu.gtceu.api.item.tool.GTToolType - the
// static registry of every tool kind GregTech generates per material.
//
// This is the data spine of the tool port. Faithful to upstream's field set
// and the 35-entry registry block; the per-entry `.toolStats(b -> ...)`
// lambdas read 1:1 against upstream.
//
// Documented deviations:
//   - MC tag members (itemTags / matchTags / craftingTags / harvestTags) and
//     defaultActions (Forge ToolAction) -> dropped. Terraria has no tag system
//     and tools are power-based, not tag-gated. ToolClassNames carries enough
//     for the Terraria ToolItem to branch on.
//   - ToolConstructor / modelLocation -> dropped; the Terraria port has a
//     single unified ToolItem (see TerrariaCompat/Items/Tools).
//   - SoundEntry -> a plain sound-name string. ExistingSoundEntry (crowbar's
//     vanilla ITEM_BREAK) -> null (ToolItem falls back to a vanilla sound).
//   - electricTier stored as the GTValues voltage index (ULV=0, LV=1, MV=2,
//     HV=3, EV=4, IV=5); -1 = not electric.
public sealed class GTToolType
{
	// GTValues.M - one material unit (= one ingot's worth). materialAmount is
	// expressed as a multiple of this; only consumed by tool recipes.
	private const long M = 3628800;

	// GTValues voltage indices, for `.Electric(...)`.
	private const int LV = 1, MV = 2, HV = 3, EV = 4, IV = 5;

	public static readonly Dictionary<string, GTToolType> Types = new();
	private static readonly Dictionary<char, GTToolType> Symbols = new();

	public readonly string Name;
	public readonly string IdFormat;
	public readonly char Symbol;
	public readonly HashSet<GTToolType> ToolClasses;
	public readonly HashSet<string> ToolClassNames;
	public readonly ToolDefinition Definition;
	public readonly string? SoundName;
	public readonly bool PlaySoundOnBlockDestroy;
	public readonly int ElectricTier;
	public readonly long MaterialAmount;

	private GTToolType(string name, string idFormat, char symbol,
		HashSet<GTToolType> toolClasses, HashSet<string> toolClassNames,
		ToolDefinition definition, string? soundName, bool playSoundOnBlockDestroy,
		int electricTier, long materialAmount)
	{
		Name = name;
		IdFormat = idFormat;
		Symbol = symbol;
		toolClasses.Add(this); // upstream: every type is a member of its own class set
		ToolClasses = toolClasses;
		ToolClassNames = toolClassNames;
		Definition = definition;
		SoundName = soundName;
		PlaySoundOnBlockDestroy = playSoundOnBlockDestroy;
		ElectricTier = electricTier;
		MaterialAmount = materialAmount;

		Types[name] = this;
	}

	public bool IsElectric => ElectricTier >= 0;

	// Builds a tool item's upstream id from a material id via the idFormat
	// ("%s_pickaxe" + "iron" -> "iron_pickaxe"; "lv_%s_drill" -> "lv_iron_drill").
	public string ResolveId(string materialId) => IdFormat.Replace("%s", materialId);

	public static GTToolType? Get(string name) => Types.GetValueOrDefault(name);

	public static GTToolType? FromSymbol(char symbol) => Symbols.GetValueOrDefault(symbol);

	private static Builder Builder_(string name) => new(name);

	// === Builder - verbatim mirror of upstream GTToolType.Builder ===========

	public sealed class Builder
	{
		private readonly string _name;
		private string _idFormat;
		private char _symbol = ' ';
		private readonly HashSet<string> _toolClassNames = new();
		private readonly HashSet<GTToolType> _toolClasses = new();
		private ToolDefinition? _toolStats;
		private long _materialAmount;
		private int _electricTier = -1;
		private string? _sound;
		private bool _playSoundOnBlockDestroy;

		public Builder(string name)
		{
			_name = name;
			_idFormat = "%s_" + name;
		}

		public Builder IdFormat(string idFormat)
		{
			_idFormat = idFormat;
			return this;
		}

		public Builder ToolStats(Func<ToolDefinition.Builder, ToolDefinition.Builder> stats)
		{
			_toolStats = stats(new ToolDefinition.Builder()).Build();
			return this;
		}

		public Builder ToolClassNames(params string[] classes)
		{
			foreach (var c in classes) _toolClassNames.Add(c);
			return this;
		}

		public Builder ToolClasses(params GTToolType[] classes)
		{
			foreach (var c in classes)
			{
				_toolClasses.Add(c);
				_toolClassNames.Add(c.Name);
			}
			return this;
		}

		public Builder MaterialAmount(long amount)
		{
			_materialAmount = amount;
			return this;
		}

		public Builder Sound(string? sound, bool playSoundOnBlockDestroy = false)
		{
			_sound = sound;
			_playSoundOnBlockDestroy = playSoundOnBlockDestroy;
			return this;
		}

		public Builder Symbol(char symbol)
		{
			_symbol = symbol;
			return this;
		}

		public Builder Electric(int tier)
		{
			_electricTier = tier;
			return this;
		}

		public GTToolType Build()
		{
			if (_toolClassNames.Count == 0) _toolClassNames.Add(_name);
			if (_symbol != ' ' && Symbols.ContainsKey(_symbol))
				throw new ArgumentException(
					$"Symbol {_symbol} has been taken by {Symbols[_symbol].Name} already!");

			var type = new GTToolType(_name, _idFormat, _symbol,
				_toolClasses, _toolClassNames, _toolStats ?? new ToolDefinition.Builder().Build(),
				_sound, _playSoundOnBlockDestroy, _electricTier, _materialAmount);

			if (_symbol != ' ') Symbols[_symbol] = type;
			return type;
		}
	}

	// === Registry - 1:1 with upstream GTToolType's static block =============

	public static readonly GTToolType SWORD = Builder_("sword")
		.ToolStats(b => b.Attacking().AttackDamage(3.0f).AttackSpeed(-2.4f))
		.ToolClassNames("sword")
		.MaterialAmount(2 * M)
		.Build();

	public static readonly GTToolType PICKAXE = Builder_("pickaxe")
		.ToolStats(b => b.BlockBreaking().AttackDamage(1.0f).AttackSpeed(-2.8f)
			.Behaviors("torch_place"))
		.ToolClassNames("pickaxe")
		.MaterialAmount(3 * M)
		.Build();

	public static readonly GTToolType SHOVEL = Builder_("shovel")
		.ToolStats(b => b.BlockBreaking().AttackDamage(1.5f).AttackSpeed(-3.0f)
			.Behaviors("grass_path", "douse_campfire"))
		.ToolClassNames("shovel")
		.MaterialAmount(M)
		.Build();

	public static readonly GTToolType AXE = Builder_("axe")
		.ToolStats(b => b.BlockBreaking()
			.AttackDamage(5.0f).AttackSpeed(-3.2f).BaseEfficiency(2.0f)
			.Behaviors("disable_shield", "tree_felling", "log_strip", "scrape", "wax_off"))
		.ToolClassNames("axe")
		.MaterialAmount(3 * M)
		.Build();

	public static readonly GTToolType HOE = Builder_("hoe")
		.ToolStats(b => b.CannotAttack().AttackSpeed(-1.0f).Behaviors("hoe_ground"))
		.ToolClassNames("hoe")
		.MaterialAmount(2 * M)
		.Build();

	public static readonly GTToolType MINING_HAMMER = Builder_("mining_hammer")
		.ToolStats(b => b.BlockBreaking().Aoe(1, 1, 0)
			.EfficiencyMultiplier(0.4f).AttackDamage(1.5f).AttackSpeed(-3.2f)
			.DurabilityMultiplier(3.0f)
			.Behaviors("aoe_config_ui", "torch_place"))
		.ToolClasses(PICKAXE)
		.MaterialAmount(6 * M)
		.Build();

	public static readonly GTToolType SPADE = Builder_("spade")
		.ToolStats(b => b.BlockBreaking().Aoe(1, 1, 0)
			.EfficiencyMultiplier(0.4f).AttackDamage(1.5f).AttackSpeed(-3.2f)
			.DurabilityMultiplier(3.0f)
			.Behaviors("aoe_config_ui", "grass_path", "douse_campfire"))
		.ToolClasses(SHOVEL)
		.MaterialAmount(3 * M)
		.Build();

	public static readonly GTToolType SCYTHE = Builder_("scythe")
		.ToolStats(b => b.BlockBreaking().Attacking()
			.AttackDamage(5.0f).AttackSpeed(-3.0f).DurabilityMultiplier(3.0f)
			.Aoe(2, 2, 2)
			.Behaviors("aoe_config_ui", "hoe_ground", "harvest_crops"))
		.ToolClassNames("scythe")
		.ToolClasses(HOE)
		.MaterialAmount(3 * M)
		.Build();

	public static readonly GTToolType SAW = Builder_("saw")
		.ToolStats(b => b.Crafting().DamagePerCraftingAction(2)
			.AttackDamage(-1.0f).AttackSpeed(-2.6f)
			.Behaviors("harvest_ice"))
		.Sound("saw")
		.Symbol('s')
		.MaterialAmount(2 * M)
		.Build();

	public static readonly GTToolType HARD_HAMMER = Builder_("hammer")
		.ToolStats(b => b.BlockBreaking().Crafting().DamagePerCraftingAction(2)
			.AttackDamage(1.0f).AttackSpeed(-2.8f)
			.Behaviors("entity_damage", "prospecting"))
		.Sound("forge_hammer")
		.Symbol('h')
		.ToolClasses(PICKAXE)
		.MaterialAmount(6 * M)
		.Build();

	public static readonly GTToolType SOFT_MALLET = Builder_("mallet")
		.ToolStats(b => b.Crafting().CannotAttack().AttackSpeed(-2.4f).SneakBypassUse()
			.Behaviors("tool_mode_switch"))
		.Sound("soft_mallet")
		.Symbol('r')
		.MaterialAmount(6 * M)
		.Build();

	public static readonly GTToolType WRENCH = Builder_("wrench")
		.ToolStats(b => b.BlockBreaking().Crafting().SneakBypassUse()
			.AttackDamage(1.0f).AttackSpeed(-2.8f)
			.Behaviors("block_rotating", "entity_damage", "tool_mode_switch"))
		.Sound("wrench", true)
		.Symbol('w')
		.MaterialAmount(4 * M)
		.Build();

	public static readonly GTToolType FILE = Builder_("file")
		.ToolStats(b => b.Crafting().DamagePerCraftingAction(4)
			.CannotAttack().AttackSpeed(-2.4f))
		.Sound("file")
		.Symbol('f')
		.MaterialAmount(2 * M)
		.Build();

	public static readonly GTToolType CROWBAR = Builder_("crowbar")
		.ToolStats(b => b.BlockBreaking().Crafting()
			.AttackDamage(2.0f).AttackSpeed(-2.4f)
			.SneakBypassUse().Behaviors("rotate_rail"))
		.Sound(null, true) // upstream: vanilla ITEM_BREAK; ToolItem uses a vanilla sound
		.Symbol('c')
		.MaterialAmount(3 * M / 2)
		.Build();

	public static readonly GTToolType SCREWDRIVER = Builder_("screwdriver")
		.ToolStats(b => b.Crafting().DamagePerCraftingAction(4).SneakBypassUse()
			.AttackDamage(-1.0f).AttackSpeed(3.0f)
			.Behaviors("entity_damage"))
		.Sound("screwdriver")
		.Symbol('d')
		.MaterialAmount(M)
		.Build();

	public static readonly GTToolType MORTAR = Builder_("mortar")
		.ToolStats(b => b.Crafting().DamagePerCraftingAction(2).CannotAttack().AttackSpeed(-2.4f))
		.Sound("mortar")
		.Symbol('m')
		.MaterialAmount(2 * M)
		.Build();

	public static readonly GTToolType WIRE_CUTTER = Builder_("wire_cutter")
		.ToolStats(b => b.BlockBreaking().Crafting().SneakBypassUse()
			.DamagePerCraftingAction(4).AttackDamage(-1.0f).AttackSpeed(-2.4f))
		.Sound("wirecutter", true)
		.Symbol('x')
		.MaterialAmount(4 * M)
		.Build();

	public static readonly GTToolType KNIFE = Builder_("knife")
		.ToolStats(b => b.Crafting().Attacking().AttackSpeed(3.0f))
		.Symbol('k')
		.ToolClasses(SWORD)
		.MaterialAmount(M)
		.Build();

	public static readonly GTToolType BUTCHERY_KNIFE = Builder_("butchery_knife")
		.ToolStats(b => b.Attacking().AttackDamage(1.5f).AttackSpeed(-1.3f))
		.MaterialAmount(4 * M)
		.Build();

	public static readonly GTToolType PLUNGER = Builder_("plunger")
		.ToolStats(b => b.CannotAttack().AttackSpeed(-2.4f).SneakBypassUse()
			.Behaviors("plunger"))
		.Sound("plunger")
		.Build();

	public static readonly GTToolType SHEARS = Builder_("shears")
		.ToolStats(b => b)
		.Build();

	public static readonly GTToolType DRILL_LV = Builder_("lv_drill")
		.IdFormat("lv_%s_drill")
		.ToolStats(b => b.BlockBreaking().Aoe(1, 1, 0)
			.AttackDamage(1.0f).AttackSpeed(-3.2f).DurabilityMultiplier(3.0f)
			.BrokenStack("power_unit_lv")
			.Behaviors("aoe_config_ui", "torch_place"))
		.Sound("drill", true)
		.Electric(LV)
		.ToolClassNames("drill")
		.Build();

	public static readonly GTToolType DRILL_MV = Builder_("mv_drill")
		.IdFormat("mv_%s_drill")
		.ToolStats(b => b.BlockBreaking().Aoe(1, 1, 2)
			.AttackDamage(1.0f).AttackSpeed(-3.2f).DurabilityMultiplier(4.0f)
			.BrokenStack("power_unit_mv")
			.Behaviors("aoe_config_ui", "torch_place"))
		.Sound("drill", true)
		.Electric(MV)
		.ToolClassNames("drill")
		.Build();

	public static readonly GTToolType DRILL_HV = Builder_("hv_drill")
		.IdFormat("hv_%s_drill")
		.ToolStats(b => b.BlockBreaking().Aoe(2, 2, 4)
			.AttackDamage(1.0f).AttackSpeed(-3.2f).DurabilityMultiplier(5.0f)
			.BrokenStack("power_unit_hv")
			.Behaviors("aoe_config_ui", "torch_place"))
		.Sound("drill", true)
		.Electric(HV)
		.ToolClassNames("drill")
		.Build();

	public static readonly GTToolType DRILL_EV = Builder_("ev_drill")
		.IdFormat("ev_%s_drill")
		.ToolStats(b => b.BlockBreaking().Aoe(3, 3, 6)
			.AttackDamage(1.0f).AttackSpeed(-3.2f).DurabilityMultiplier(6.0f)
			.BrokenStack("power_unit_ev")
			.Behaviors("aoe_config_ui", "torch_place"))
		.Sound("drill", true)
		.Electric(EV)
		.ToolClassNames("drill")
		.Build();

	public static readonly GTToolType DRILL_IV = Builder_("iv_drill")
		.IdFormat("iv_%s_drill")
		.ToolStats(b => b.BlockBreaking().Aoe(4, 4, 8)
			.AttackDamage(1.0f).AttackSpeed(-3.2f).DurabilityMultiplier(7.0f)
			.BrokenStack("power_unit_iv")
			.Behaviors("aoe_config_ui", "torch_place"))
		.Sound("drill", true)
		.Electric(IV)
		.ToolClassNames("drill")
		.Build();

	public static readonly GTToolType CHAINSAW_LV = Builder_("lv_chainsaw")
		.IdFormat("lv_%s_chainsaw")
		.ToolStats(b => b.BlockBreaking()
			.EfficiencyMultiplier(2.0f)
			.AttackDamage(5.0f).AttackSpeed(-3.2f)
			.BrokenStack("power_unit_lv")
			.Behaviors("harvest_ice", "disable_shield", "tree_felling"))
		.Sound("chainsaw", true)
		.Electric(LV)
		.ToolClasses(AXE)
		.Build();

	public static readonly GTToolType CHAINSAW_HV = Builder_("hv_chainsaw")
		.IdFormat("hv_%s_chainsaw")
		.ToolStats(b => b.BlockBreaking()
			.EfficiencyMultiplier(3.0f)
			.AttackDamage(5.0f).AttackSpeed(-3.2f)
			.BrokenStack("power_unit_hv")
			.Behaviors("harvest_ice", "disable_shield", "tree_felling"))
		.Sound("chainsaw", true)
		.Electric(HV)
		.ToolClasses(AXE)
		.Build();

	public static readonly GTToolType CHAINSAW_IV = Builder_("iv_chainsaw")
		.IdFormat("iv_%s_chainsaw")
		.ToolStats(b => b.BlockBreaking()
			.EfficiencyMultiplier(4.0f)
			.AttackDamage(5.0f).AttackSpeed(-3.2f)
			.BrokenStack("power_unit_iv")
			.Behaviors("harvest_ice", "disable_shield", "tree_felling"))
		.Sound("chainsaw", true)
		.Electric(IV)
		.ToolClasses(AXE)
		.Build();

	public static readonly GTToolType WRENCH_LV = Builder_("lv_wrench")
		.IdFormat("lv_%s_wrench")
		.ToolStats(b => b.BlockBreaking().Crafting().SneakBypassUse()
			.EfficiencyMultiplier(2.0f)
			.AttackDamage(1.0f).AttackSpeed(-2.8f)
			.Behaviors("block_rotating", "entity_damage", "tool_mode_switch")
			.BrokenStack("power_unit_lv"))
		.Sound("wrench", true)
		.Electric(LV)
		.ToolClasses(WRENCH)
		.Build();

	public static readonly GTToolType WRENCH_HV = Builder_("hv_wrench")
		.IdFormat("hv_%s_wrench")
		.ToolStats(b => b.BlockBreaking().Crafting().SneakBypassUse()
			.EfficiencyMultiplier(3.0f)
			.AttackDamage(1.0f).AttackSpeed(-2.8f)
			.Behaviors("block_rotating", "entity_damage", "tool_mode_switch")
			.BrokenStack("power_unit_hv"))
		.Sound("wrench", true)
		.Electric(HV)
		.ToolClasses(WRENCH)
		.Build();

	public static readonly GTToolType WRENCH_IV = Builder_("iv_wrench")
		.IdFormat("iv_%s_wrench")
		.ToolStats(b => b.BlockBreaking().Crafting().SneakBypassUse()
			.EfficiencyMultiplier(4.0f)
			.AttackDamage(1.0f).AttackSpeed(-2.8f)
			.Behaviors("block_rotating", "entity_damage", "tool_mode_switch")
			.BrokenStack("power_unit_iv"))
		.Sound("wrench", true)
		.Electric(IV)
		.ToolClasses(WRENCH)
		.Build();

	public static readonly GTToolType WIRE_CUTTER_LV = Builder_("lv_wirecutter")
		.IdFormat("lv_%s_wire_cutter")
		.ToolStats(b => b.BlockBreaking().Crafting().SneakBypassUse()
			.DamagePerCraftingAction(4).AttackDamage(-1.0f).AttackSpeed(-2.4f)
			.BrokenStack("power_unit_lv"))
		.Sound("wirecutter", true)
		.Electric(LV)
		.ToolClasses(WIRE_CUTTER)
		.Build();

	public static readonly GTToolType WIRE_CUTTER_HV = Builder_("hv_wirecutter")
		.IdFormat("hv_%s_wire_cutter")
		.ToolStats(b => b.BlockBreaking().Crafting().SneakBypassUse()
			.DamagePerCraftingAction(4).AttackDamage(-1.0f).AttackSpeed(-2.4f)
			.BrokenStack("power_unit_hv"))
		.Sound("wirecutter", true)
		.Electric(HV)
		.ToolClasses(WIRE_CUTTER)
		.Build();

	public static readonly GTToolType WIRE_CUTTER_IV = Builder_("iv_wirecutter")
		.IdFormat("iv_%s_wire_cutter")
		.ToolStats(b => b.BlockBreaking().Crafting().SneakBypassUse()
			.DamagePerCraftingAction(4).AttackDamage(-1.0f).AttackSpeed(-2.4f)
			.BrokenStack("power_unit_iv"))
		.Sound("wirecutter", true)
		.Electric(IV)
		.ToolClasses(WIRE_CUTTER)
		.Build();

	public static readonly GTToolType BUZZSAW = Builder_("buzzsaw")
		.ToolStats(b => b.Crafting().AttackDamage(1.5f).AttackSpeed(-3.2f)
			.BrokenStack("power_unit_lv"))
		.Sound("chainsaw", true)
		.Electric(LV)
		.ToolClasses(SAW)
		.Build();

	public static readonly GTToolType SCREWDRIVER_LV = Builder_("lv_screwdriver")
		.IdFormat("lv_%s_screwdriver")
		.ToolStats(b => b.Crafting().SneakBypassUse()
			.AttackDamage(-1.0f).AttackSpeed(3.0f)
			.Behaviors("entity_damage")
			.BrokenStack("power_unit_lv"))
		.Sound("screwdriver")
		.Electric(LV)
		.ToolClasses(SCREWDRIVER)
		.Build();

	public static readonly GTToolType SCREWDRIVER_HV = Builder_("hv_screwdriver")
		.IdFormat("hv_%s_screwdriver")
		.ToolStats(b => b.Crafting().SneakBypassUse()
			.AttackDamage(-1.0f).AttackSpeed(3.0f)
			.Behaviors("entity_damage")
			.BrokenStack("power_unit_hv"))
		.Sound("screwdriver")
		.Electric(HV)
		.ToolClasses(SCREWDRIVER)
		.Build();

	public static readonly GTToolType SCREWDRIVER_IV = Builder_("iv_screwdriver")
		.IdFormat("iv_%s_screwdriver")
		.ToolStats(b => b.Crafting().SneakBypassUse()
			.AttackDamage(-1.0f).AttackSpeed(3.0f)
			.Behaviors("entity_damage")
			.BrokenStack("power_unit_iv"))
		.Sound("screwdriver")
		.Electric(IV)
		.ToolClasses(SCREWDRIVER)
		.Build();
}
