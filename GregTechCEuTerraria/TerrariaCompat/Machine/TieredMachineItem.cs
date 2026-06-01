#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Universal def-driven machine Item. One class, registered per (def, tier)
// by TieredMachineFactory. Parameterless ctor is the autoload probe.
public class TieredMachineItem : TieredItem, Rendering.ITextureWarmUp
{
	[CloneByReference] protected readonly MachineDefinition? _def;

	public TieredMachineItem() { }
	public TieredMachineItem(VoltageTier tier, MachineDefinition def) : base(tier) { _def = def; }

	protected override string SnakeName => _def?.Id ?? GetType().Name;

	// Non-tiered = bare material id (drum/crate).
	public override string Name =>
		_def != null && !_def.Tiered ? _def.Id : base.Name;

	// PreDraw composites the real icon.
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsItem";

	public override void SetStaticDefaults()
	{
		base.SetStaticDefaults();
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => _def!.Tiered ? $"{VoltageTiers.ShortName(_tier)} {_def.Label}" : _def.Label);
	}

	public override void SetDefaults()
	{
		int tileType = Mod.TryFind<ModTile>(Name, out var t) ? t.Type : 0;
		Item.DefaultToPlaceableTile(tileType);
		Item.maxStack = 99;
		Item.width = 32;
		Item.height = 32;
		Item.rare = ItemRarityID.White;
	}

	private IMachineTextureSpec? SiblingSpec()
	{
		if (Mod.TryFind<ModTile>(Name, out var modTile) && modTile is IMachineTextureSpec spec)
			return spec;
		return null;
	}

	// Transformer overrides for its 2-face composite icon.
	public virtual void WarmUpTexture()
	{
		var spec = SiblingSpec();
		if (spec is null) return;
		MachineRenderer.EnsureItemTexture(Item.type, spec, _tier);
		if (Mod.TryFind<ModTile>(Name, out var t))
			MachineRenderer.EnsureTileTexture(t.Type, spec, _tier);
	}

	public override bool PreDrawInInventory(Microsoft.Xna.Framework.Graphics.SpriteBatch sb, Microsoft.Xna.Framework.Vector2 position, Microsoft.Xna.Framework.Rectangle frame, Microsoft.Xna.Framework.Color drawColor, Microsoft.Xna.Framework.Color itemColor, Microsoft.Xna.Framework.Vector2 origin, float scale)
	{
		WarmUpTexture();
		return true;
	}

	public override bool PreDrawInWorld(Microsoft.Xna.Framework.Graphics.SpriteBatch sb, Microsoft.Xna.Framework.Color lightColor, Microsoft.Xna.Framework.Color alphaColor, ref float rotation, ref float scale, int whoAmI)
	{
		WarmUpTexture();
		return true;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		tooltips.ApplyTierColor(_tier);
		AppendTierTooltip(tooltips);
		AppendMachineTooltipBuilder(tooltips);
		AppendHpcaComponentTooltip(tooltips);
	}

	// Stats from HPCAComponentPartMachine SSOT (upstream's locale keys not mirrored).
	private void AppendHpcaComponentTooltip(List<TooltipLine> tooltips)
	{
		if (_def?.Family != MachineFamily.HpcaComponent || _def.HpcaKind is not { } kind) return;
		long Va(VoltageTier t) => VoltageTiers.VA((int)t);
		void L(string s) => tooltips.Add(new TooltipLine(Mod, $"Hpca{tooltips.Count}", s));
		switch (kind)
		{
			case Multiblock.Part.Hpca.HpcaComponentKind.Empty:
				L("[c/AAAAAA:Filler - no function. Fill unused HPCA grid slots so the structure forms.]");
				break;
			case Multiblock.Part.Hpca.HpcaComponentKind.Computation:
				L("[c/55AAFF:Computation: 4 CWU/t]");
				L("[c/FFAA55:Produces heat under load]");
				L($"[c/AAAAAA:EU upkeep {Va(VoltageTier.EV):N0} -> {Va(VoltageTier.LuV):N0} EU/t]");
				break;
			case Multiblock.Part.Hpca.HpcaComponentKind.AdvancedComputation:
				L("[c/55AAFF:Computation: 16 CWU/t]");
				L("[c/FFAA55:Produces heat under load]");
				L($"[c/AAAAAA:EU upkeep {Va(VoltageTier.IV):N0} -> {Va(VoltageTier.ZPM):N0} EU/t]");
				break;
			case Multiblock.Part.Hpca.HpcaComponentKind.HeatSink:
				L("[c/55FFFF:Cooling: 1 (passive - no power or coolant)]");
				break;
			case Multiblock.Part.Hpca.HpcaComponentKind.ActiveCooler:
				L("[c/55FFFF:Cooling: 2]");
				L("[c/55AAFF:Coolant: up to 8 mB/t (pcb_coolant)]");
				L($"[c/AAAAAA:EU upkeep {Va(VoltageTier.IV):N0} EU/t]");
				break;
			case Multiblock.Part.Hpca.HpcaComponentKind.Bridge:
				L("[c/55FF55:Enables HPCA bridging (chain via Network Switch)]");
				L($"[c/AAAAAA:EU upkeep {Va(VoltageTier.IV):N0} EU/t]");
				break;
		}
	}

	// = MetaMachineItem.appendHoverText.
	private void AppendMachineTooltipBuilder(List<TooltipLine> tooltips)
	{
		var buf = new List<string>();
		MachineTooltipLookup.AppendDescriptionAndBuilder(buf, Name, _def?.Id, _def);
		for (int i = 0; i < buf.Count; i++)
			tooltips.Add(new TooltipLine(Mod, $"MachineTip{i}", buf[i]));
	}

	protected virtual void AppendTierTooltip(List<TooltipLine> tooltips)
	{
		// Energy hatches: V x A throughput; the per-amp default under-reports.
		if (_def?.Family == MachineFamily.EnergyHatch && _def.PartAmperage > 0)
		{
			long amps = _def.PartAmperage;
			long volts = VoltageTiers.Voltage(_tier);
			tooltips.Add(new TooltipLine(Mod, "TierLine",
				$"{VoltageTiers.ShortName(_tier)} - {amps}A, max [c/CC55FF:{volts * amps:N0}] EU/t ({volts:N0} EU/t x {amps}A)"));
			return;
		}

		tooltips.Add(new TooltipLine(Mod, "TierLine",
			$"{VoltageTiers.ShortName(_tier)} - cap {VoltageTiers.Voltage(_tier):N0} EU/t"));

		// Verbatim GCYMMachines.java:79-80 .tooltips chain.
		if (_def?.Family == MachineFamily.ParallelHatch)
		{
			int maxParallel = (int)System.Math.Pow(4, (int)_tier - (int)VoltageTier.EV);
			tooltips.Add(new TooltipLine(Mod, "ParallelMax",
				$"Allows up to [c/CC55FF:{maxParallel}x] parallel recipes"));
			tooltips.Add(new TooltipLine(Mod, "PartSharingDisabled",
				"[c/AAAAAA:Cannot be shared between multiblocks]"));
		}

		// Miner / SteamMiner area+speed+draw (item hover = structure; world hover
		// = live state). Tier formulas mirror MinerMachine / SteamMinerMachine
		// (SSOT). Depth is world-height-scaled - fall back to 1200 at menu.
		int h = Terraria.Main.maxTilesY > 0 ? Terraria.Main.maxTilesY : 1200;
		if (_def?.Family == MachineFamily.Miner)
		{
			int tier = (int)_tier;
			int width = tier switch { 1 => 32, 2 => 48, 3 => 64, 4 => 128, _ => 16 * tier };
			double frac = tier switch { 1 => 0.35, 2 => 0.55, 3 => 0.75, 4 => 0.95, _ => 0.35 + 0.20 * (tier - 1) };
			int depth = (int)(h * frac);
			int ticksPerOre = System.Math.Max(5, 45 - 8 * tier);
			long euPerTick = VoltageTiers.Voltage((VoltageTier)System.Math.Max(0, tier - 1));
			tooltips.Add(new TooltipLine(Mod, "MinerArea",
				$"Mining area: [c/CC55FF:{width}] wide x [c/CC55FF:{depth}] deep (below the machine)"));
			tooltips.Add(new TooltipLine(Mod, "MinerSpeed",
				$"Speed: [c/CC55FF:{ticksPerOre}] ticks / ore"));
			tooltips.Add(new TooltipLine(Mod, "MinerDraw",
				$"Draw: [c/CC55FF:{euPerTick:N0}] EU/t while mining"));
		}
		else if (_def?.Family == MachineFamily.SteamMiner)
		{
			bool hp = _def.IsHighPressure;
			int width = hp ? 24   : 16;
			double frac = hp ? 0.55 : 0.30;
			int depth = (int)(h * frac);
			int ticksPerOre = hp ? 120 : 160;
			int steamPerTick = hp ? 4 : 2;
			tooltips.Add(new TooltipLine(Mod, "MinerArea",
				$"Mining area: [c/CC55FF:{width}] wide x [c/CC55FF:{depth}] deep (below the machine)"));
			tooltips.Add(new TooltipLine(Mod, "MinerSpeed",
				$"Speed: [c/CC55FF:{ticksPerOre}] ticks / ore"));
			tooltips.Add(new TooltipLine(Mod, "MinerDraw",
				$"Steam: [c/CC55FF:{steamPerTick}] mB/t while mining"));
		}
	}
}
