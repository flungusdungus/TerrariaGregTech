#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Tool;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Items.Cables;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// One (Material x GTToolType) pair, mirroring upstream's GTToolItem. Stats are
// computed in the ctor from the material's ToolProperty via verbatim IGTTool
// total-stat formulas. Durability NOT modelled (project decision; dump still
// carries the data). Electric tools store EU per-stack - same BatteryItem
// charge contract, so machine charger slots top them up unchanged.
public sealed class ToolItem : ModItem, IElectricItem
{
	private readonly string? _id;
	private readonly string? _label;
	private readonly GTToolType? _type;
	private readonly ToolLayer[] _layers = System.Array.Empty<ToolLayer>();
	private readonly string _texture = "Terraria/Images/Item_22";

	// Computed Terraria stats.
	private readonly int _pick, _axe, _hammer, _damage, _useTime;
	private readonly int _harvestLevel;
	private readonly int _tier;
	private readonly float _toolSpeed, _attackDamage;

	// Electric - 0 maxCharge for non-electric tools.
	private readonly long _maxCharge;
	private readonly long _euPerUse;
	private long _storedEu;

	public ToolItem() { }

	public ToolItem(string id, string label, GTToolType type, Material material, ToolLayer[] layers, string texture)
	{
		_id = id;
		_label = label;
		_type = type;
		_layers = layers;
		_texture = texture;

		var def = type.Definition;
		var tp = material.Tool!; // loader filters to materials with a Tool property

		// Verbatim IGTTool total-stat formulas.
		_toolSpeed = def.EfficiencyMultiplier * tp.HarvestSpeed + def.BaseEfficiency;
		_attackDamage = def.BaseDamage == ToolDefinition.NoAttackSentinel
			? 0f
			: tp.AttackDamage + def.BaseDamage;
		_harvestLevel = tp.HarvestLevel + def.BaseQuality;

		// Map onto Terraria's tool model.
		bool isPick = type.ToolClassNames.Contains("pickaxe") || type.Name.Contains("drill");
		bool isAxe = type.ToolClassNames.Contains("axe") || type.Name.Contains("chainsaw");
		bool isHammer = ReferenceEquals(type, GTToolType.HARD_HAMMER);

		// Upstream-formula stats - keeps per-material variation in tier.
		int upPick    = isPick   ? Math.Min(250, 28 + _harvestLevel * 20) : 0;
		int upAxe     = isAxe    ? Math.Max(5, 7 + _harvestLevel * 4)     : 0;
		int upHammer  = isHammer ? 35 + _harvestLevel * 20                : 0;
		int upDamage  = Math.Max(1, (int)Math.Round(2 + _attackDamage));
		// Crowbar repurposed as pure melee - Zombie-Arm damage curve.
		if (IsCrowbar) upDamage = 12 + _harvestLevel * 3;
		int upUseTime = Math.Clamp((int)Math.Round(19 - _toolSpeed * 0.6f), 9, 18);

		// Blend toward a Terraria-progression anchor (see ToolTier.AnchorBlend)
		// so per-material variation still survives at reduced amplitude.
		int tier = ToolTier.For(material);
		_tier = tier;
		var anchor = ToolTier.AnchorFor(tier);
		_pick    = isPick   ? ToolTier.Blend(upPick,    anchor.Pick)    : 0;
		_axe     = isAxe    ? ToolTier.Blend(upAxe,     anchor.Axe)     : 0;
		_hammer  = isHammer ? ToolTier.Blend(upHammer,  anchor.Hammer)  : 0;
		_damage  = Math.Max(1, ToolTier.Blend(upDamage, anchor.Damage));
		_useTime = Math.Max(2, ToolTier.Blend(upUseTime, anchor.UseTime));

		// Terraria-side identity overrides (TerrariaCompat glue, no upstream
		// parity) - gives the otherwise-samey tools distinct feel.
		string n = type.Name;
		if (n == "shovel" || n == "spade")
		{
			// Soft-digger; stone is barred by the tile-type filter in
			// ToolWorldEffects, not by raw pick power.
			_pick = Math.Max(35, anchor.Pick / 4);
			_axe = 0; _hammer = 0;
			_useTime = Math.Max(2, (int)Math.Round(_useTime / (n == "shovel" ? 3.0 : 2.0)));
		}
		else if (n == "saw" || n == "buzzsaw")
		{
			_axe = anchor.Axe;                                  // tree drops swap to rubber wood in ToolWorldEffects
		}
		else if (n == "mortar")
		{
			// 0.75x same-material pickaxe; stone drops Silt.
			int pickaxePick = ToolTier.Blend(Math.Min(250, 28 + _harvestLevel * 20), anchor.Pick);
			_pick = Math.Max(1, (int)Math.Round(pickaxePick * 0.75));
		}
		else if (n == "screwdriver" || n == "file")
		{
			// Spear-poke (Palladium-Pike thrust). File = 0.75x screwdriver.
			_damage = Math.Max(1, (int)Math.Round(anchor.Damage * (n == "file" ? 0.75 : 1.0)));
			_useTime = n == "file" ? 22 : 14;
		}
		else if (n == "plunger")
		{
			// Super-Absorbent-Sponge speed; reaches Sponge cadence ~EV/4.
			_useTime = Math.Clamp(24 - 5 * tier, 2, 60);
		}

		if (type.IsElectric)
		{
			// x4/tier from LV=100k. Upstream reads this off the assembly
			// recipe's .get(maxCharge) - placeholder until tool recipes carry it.
			_maxCharge = 100_000L * (long)Math.Pow(4, type.ElectricTier - 1);
			_euPerUse = Math.Max(1, _maxCharge / 4000);
		}
	}

	public override bool IsLoadingEnabled(Mod mod) => _id != null;
	public override string Name => _id ?? nameof(ToolItem);
	public override string Texture => _texture;
	protected override bool CloneNewInstances => true;

	public GTToolType ToolType => _type!;
	public int AoeColumn => _type?.Definition.Aoe.Column ?? 0;
	public int AoeRow => _type?.Definition.Aoe.Row ?? 0;
	public bool IsElectric => _type?.IsElectric ?? false;

	// IElectricItem - same contract as BatteryItem. Non-electric tools report
	// 0 capacity, so charger slots no-op on them.
	public long StoredEu
	{
		get => _storedEu;
		set => _storedEu = Math.Clamp(value, 0, _maxCharge);
	}

	public bool CanProvideChargeExternally() => false; // chargers fill, never drain
	public bool Chargeable() => IsElectric;
	public long GetTransferLimit() => IsElectric ? VoltageTiers.Voltage((VoltageTier)_type!.ElectricTier) : 0;
	public long GetMaxCharge() => _maxCharge;
	public long GetCharge() => _storedEu;
	public int GetTier() => _type?.ElectricTier ?? 0;

	public long Charge(long amount, int chargerTier, bool ignoreTransferLimit, bool simulate)
	{
		if (Item.stack != 1 || !IsElectric) return 0L;
		int tier = _type!.ElectricTier;
		if (chargerTier >= tier && amount > 0L)
		{
			long canReceive = _maxCharge - _storedEu;
			if (!ignoreTransferLimit) amount = Math.Min(amount, GetTransferLimit());
			long charged = Math.Min(amount, canReceive);
			if (!simulate) _storedEu += charged;
			return charged;
		}
		return 0L;
	}

	public long Discharge(long amount, int dischargerTier, bool ignoreTransferLimit, bool externally, bool simulate)
	{
		// `externally`=true blocks the tool as a power source; internal use OK.
		if (Item.stack != 1 || !IsElectric || externally) return 0L;
		int tier = _type!.ElectricTier;
		if (dischargerTier >= tier && amount > 0L)
		{
			if (!ignoreTransferLimit) amount = Math.Min(amount, GetTransferLimit());
			long discharged = Math.Min(amount, _storedEu);
			if (!simulate) _storedEu -= discharged;
			return discharged;
		}
		return 0L;
	}

	public override void SetStaticDefaults()
	{
		if (_label != null)
			Terraria.Localization.Language.GetOrRegister(
				$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName", () => _label);
	}

	// Drills/chainsaws - drawn via a rotating held projectile.
	public bool DrillLike => _type != null &&
		(_type.Name.Contains("drill") || _type.Name.Contains("chainsaw"));

	// Machine-interaction tool classifications.
	public bool IsWrench => _type != null && _type.ToolClassNames.Contains("wrench");
	public bool IsWireCutter => _type != null && _type.ToolClassNames.Contains("wire_cutter");
	public bool IsMallet => _type != null && _type.Name == "mallet";
	public bool IsCrowbar => _type != null && _type.Name == "crowbar";
	public bool IsKnife => _type != null && _type.Name == "knife";
	public bool IsButcheryKnife => _type != null && _type.Name == "butchery_knife";

	// Terraria-identity tool flags (read by ToolWorldEffects / SetDefaults).
	public int Tier => _tier;
	public bool IsSoftDigger => _type != null && (_type.Name == "shovel" || _type.Name == "spade");
	public bool IsSawLike => _type != null && (_type.Name == "saw" || _type.Name == "buzzsaw");
	public bool IsMortar => _type != null && _type.Name == "mortar";
	public bool IsHoe => _type != null && _type.Name == "hoe";
	public bool IsScythe => _type != null && _type.Name == "scythe";
	public bool IsSword => _type != null && _type.Name == "sword";
	public bool IsPoker => _type != null && (_type.Name == "screwdriver" || _type.Name == "file");
	public bool IsPlunger => _type != null && _type.Name == "plunger";

	public override void SetDefaults()
	{
		Item.maxStack = 1;
		Item.width = Item.height = 32;
		Item.DamageType = DamageClass.Melee;
		Item.damage = _damage;
		Item.knockBack = IsCrowbar ? 9.5f : 3f;
		Item.useTime = Item.useAnimation = _useTime;
		// Auto-attack on for combat/mining tools. Utility one-shots stay
		// click-only so a held LMB doesn't fire the action 30 times/sec.
		bool isUtilityOneShot = IsWrench || IsWireCutter || IsMallet
			|| (_type != null && _type.Name == "soft_hammer");
		Item.autoReuse = !isUtilityOneShot;
		Item.pick = _pick;
		Item.axe = _axe;
		Item.hammer = _hammer;
		Item.rare = Math.Clamp(_harvestLevel, ItemRarityID.White, ItemRarityID.Purple);
		Item.value = Item.sellPrice(silver: 5 + _harvestLevel * 3);

		if (DrillLike)
		{
			// Held projectile rotates to aim direction; item graphic hidden,
			// DrillHeldProjectile draws it in-hand. Inventory icon untouched.
			Item.useStyle = ItemUseStyleID.Shoot;
			Item.DamageType = DamageClass.MeleeNoSpeed;
			Item.shoot = ModContent.ProjectileType<DrillHeldProjectile>();
			Item.shootSpeed = 32f;
			Item.noMelee = true;
			Item.noUseGraphic = true;
			Item.channel = true;
		}
		else if (IsButcheryKnife)
		{
			// Infinite-throw knife (never consumable). ButcheryKnifeProjectile
			// runs vanilla knife AI but draws this knife (Shoot stamps Item.type).
			Item.useStyle = ItemUseStyleID.Swing;
			Item.UseSound = SoundID.Item1;
			Item.shoot = ModContent.ProjectileType<ButcheryKnifeProjectile>();
			Item.shootSpeed = 10f;
			Item.noUseGraphic = true;
			Item.noMelee = true;
			Item.autoReuse = true;
			Item.consumable = false;
		}
		else if (IsKnife)
		{
			Item.useStyle = ItemUseStyleID.Swing;
			Item.UseSound = SoundID.Item1;
			Item.autoReuse = true;
			Item.useTurn = true;
		}
		else if (IsPoker)
		{
			// Held spear projectile (vanilla AI 19); ToolPokeProjectile draws
			// this tool in-hand.
			Item.useStyle = ItemUseStyleID.Shoot;
			Item.DamageType = DamageClass.Melee;
			Item.shoot = ModContent.ProjectileType<ToolPokeProjectile>();
			Item.shootSpeed = 3.6f;
			Item.knockBack = 4.5f;
			Item.noMelee = true;
			Item.noUseGraphic = true;
			Item.autoReuse = true;
			Item.UseSound = SoundID.Item1;
		}
		else if (IsScythe)
		{
			// Wide crowd-clear (Death-Sickle feel); cordage flag set in HoldItem.
			Item.useStyle = ItemUseStyleID.Swing;
			Item.UseSound = SoundID.Item1;
			Item.autoReuse = true;
			Item.useTurn = false;
			Item.scale = 1.5f;
		}
		else
		{
			Item.useStyle = ItemUseStyleID.Swing;
			Item.useTurn = true;
			Item.UseSound = SoundID.Item1;
			// Sword scale: tier 0 (flint) = Copper Shortsword 0.8, tier 2
			// (Aluminium) = Volcano 1.0, +0.1 per tier above.
			if (IsSword) Item.scale = MathHelper.Clamp(0.8f + 0.1f * _tier, 0.8f, 2.0f);
		}
	}

	public override bool CanUseItem(Player player) =>
		!IsElectric || _storedEu >= _euPerUse;

	// Knife is the lone GT tool that hits town NPCs (flagged red in tooltip).
	// MUST return an explicit bool: tML's CombinedHooks.CanPlayerHitNPCWithItem
	// only bypasses vanilla's "only the Rotten Egg can hit town NPCs" gate when
	// a hook returns true; null falls through to that gate.
	public override bool? CanHitNPC(Player player, NPC target)
	{
		if (target.townNPC) return IsKnife;
		return null;
	}

	// ai[0] carries this knife's item type so the projectile draws THIS knife.
	public override bool Shoot(Player player, Terraria.DataStructures.EntitySource_ItemUse_WithAmmo source,
		Vector2 position, Vector2 velocity, int type, int damage, float knockback)
	{
		if (!IsButcheryKnife) return true;
		Projectile.NewProjectile(source, position, velocity, type, damage, knockback,
			player.whoAmI, ai0: Item.type);
		return false;
	}

	public override bool? UseItem(Player player)
	{
		if (IsElectric && _storedEu > 0)
			_storedEu = Math.Max(0, _storedEu - _euPerUse);

		if (player.whoAmI == Main.myPlayer && (IsWrench || IsMallet || IsWireCutter))
			DoMachineInteraction(player);

		if (player.whoAmI == Main.myPlayer && IsPlunger)
			AbsorbLiquid(player);

		return true;
	}

	// Port of vanilla absorbing-sponge (Player.cs:45724) - zero cursor tile,
	// pull matching-type liquid from 8 neighbours (cap 255). Removes every
	// liquid type. Throughput gated by tier-scaled Item.useTime.
	private void AbsorbLiquid(Player player)
	{
		int tx = Player.tileTargetX, ty = Player.tileTargetY;
		if (!WorldGen.InWorld(tx, ty, 2)) return;
		var center = Main.tile[tx, ty];
		if (center.LiquidAmount <= 0) return;

		int liquidType = center.LiquidType;
		int collected = center.LiquidAmount;
		center.LiquidAmount = 0;
		center.LiquidType = LiquidID.Water;
		WorldGen.SquareTileFrame(tx, ty, resetFrame: false);
		if (Main.netMode == NetmodeID.MultiplayerClient) NetMessage.sendWater(tx, ty);
		else Liquid.AddWater(tx, ty);
		SoundEngine.PlaySound(SoundID.Splash, new Vector2(tx * 16f, ty * 16f));

		if (collected >= 255) return;
		for (int k = tx - 1; k <= tx + 1; k++)
		for (int l = ty - 1; l <= ty + 1; l++)
		{
			if (k == tx && l == ty) continue;
			if (!WorldGen.InWorld(k, l, 2)) continue;
			var t = Main.tile[k, l];
			if (t.LiquidAmount <= 0 || t.LiquidType != liquidType) continue;
			int take = t.LiquidAmount;
			if (take + collected > 255) take = 255 - collected;
			collected += take;
			t.LiquidAmount -= (byte)take;
			if (t.LiquidAmount == 0) t.LiquidType = LiquidID.Water;
			WorldGen.SquareTileFrame(k, l, resetFrame: false);
			if (Main.netMode == NetmodeID.MultiplayerClient) NetMessage.sendWater(k, l);
			else Liquid.AddWater(k, l);
		}
	}

	// Owning-client only; mutations go through action packets / TileManipulation
	// sync, never direct shared-state writes.
	private void DoMachineInteraction(Player player)
	{
		int x = (int)(Main.MouseWorld.X / 16f);
		int y = (int)(Main.MouseWorld.Y / 16f);
		if (x <= 0 || x >= Main.maxTilesX - 1 || y <= 0 || y >= Main.maxTilesY - 1) return;

		if (IsWireCutter)
		{
			if (WireItem.CutCableAt(player, x, y)) return;
			// Upstream parity: laser pipe tune-tool = wire cutter.
			Pipelike.Laser.LaserPipeLayerHandle.Instance.CutAt(x, y, player);
			return;
		}

		if (Vector2.Distance(player.Center, new Vector2(x * 16f + 8f, y * 16f + 8f)) > 16f * 12f)
			return;

		if (IsWrench)
		{
			// Upstream parity: item + fluid pipe tune-tool = wrench. Pipes
			// checked FIRST; machine dismantle below falls through cleanly.
			if (Pipelike.ItemPipe.ItemPipeLayerHandle.Instance.CutAt(x, y, player)) return;
			if (Pipelike.Fluid.FluidPipeLayerHandle.Instance.CutAt(x, y, player)) return;
			if (Pipelike.LongDistance.LongDistancePipeLayerHandle.Item.CutAt(x, y, player)) return;
		}

		if (!MachineCellResolver.TryFindMachineAt(x, y, out var machine)) return;

		if (IsWrench)
		{
			// Break at origin so multi-tile + entity go in one hit.
			var pos = machine.Position;
			WorldGen.KillTile(pos.X, pos.Y);
			if (Main.netMode == NetmodeID.MultiplayerClient)
				NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, pos.X, pos.Y);
		}
		else // IsMallet
		{
			// Audible/CombatText feedback so the toggle is visible even when
			// the machine is idle and has no running-state change.
			bool newState = !machine.WorkingEnabled;
			MachineActions.Send(new PowerToggleAction(newState), machine);

			var p = machine.Position;
			var worldPos = new Vector2(p.X * 16f, p.Y * 16f);
			SoundEngine.PlaySound(SoundID.Mech, worldPos);
			CombatText.NewText(new Rectangle(p.X * 16, p.Y * 16, 32, 32),
				newState ? Color.LightGreen : Color.OrangeRed,
				newState ? "Enabled" : "Disabled");
		}
	}

	public override ModItem Clone(Item newEntity)
	{
		var clone = (ToolItem)base.Clone(newEntity);
		clone._storedEu = _storedEu;
		return clone;
	}

	public override void SaveData(TagCompound tag)
	{
		if (IsElectric) tag["eu"] = _storedEu;
	}

	public override void LoadData(TagCompound tag)
	{
		_storedEu = IsElectric && tag.ContainsKey("eu") ? tag.GetLong("eu") : 0;
	}

	public override void NetSend(System.IO.BinaryWriter writer)
	{
		if (IsElectric) writer.Write(_storedEu);
	}

	public override void NetReceive(System.IO.BinaryReader reader)
	{
		if (IsElectric) _storedEu = reader.ReadInt64();
	}

	// Held-tool draw goes straight from TextureAssets.Item (no PreDraw hook
	// reaches it), so the upstream layer composite is baked once and installed
	// there - vanilla then draws it identically in slots, on ground, in-hand.
	public override bool PreDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame,
		Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		EnsureTextureBaked();
		return true;
	}

	public override bool PreDrawInWorld(SpriteBatch sb, Color lightColor, Color alphaColor,
		ref float rotation, ref float scale, int whoAmI)
	{
		EnsureTextureBaked();
		return true;
	}

	// Bake before held-item draw + flip vanilla plant-harvest flags so the herb/
	// vine drop code (KillTile_GetItemDrops) treats this tool like its analogue.
	public override void HoldItem(Player player)
	{
		EnsureTextureBaked();
		// Scythe: Guide-To-Plant-Fiber-Cordage parity (50% Vine drop).
		if (IsScythe) player.cordage = true;
		// Hoe herb-seed bonus is in ToolWorldEffects.Drop (the vanilla
		// staffOfRegrowthBonus flag isn't exposed to mods).
	}

	// Public so callers that render this tool outside the item-draw pipeline
	// (e.g. the Gregith projectile) can pre-bake. Idempotent.
	public void EnsureTextureBaked()
	{
		if (_layers.Length == 0) return;
		var iconLayers = new IconLayer[_layers.Length];
		for (int i = 0; i < _layers.Length; i++)
			iconLayers[i] = new IconLayer(_layers[i].TexturePath, _layers[i].Tint);
		ItemIconBaker.Install(Item.type, iconLayers);
	}

	// Charge bar - same readout as BatteryItem.
	public override void PostDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame,
		Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		if (IsElectric) DrawChargeBar(sb, position, scale);
	}

	public override void PostDrawInWorld(SpriteBatch sb, Color lightColor, Color alphaColor,
		float rotation, float scale, int whoAmI)
	{
		if (IsElectric) DrawChargeBar(sb, Item.Center - Main.screenPosition, scale);
	}

	private void DrawChargeBar(SpriteBatch sb, Vector2 center, float scale)
	{
		if (_maxCharge <= 0) return;
		float pct = Math.Clamp((float)_storedEu / _maxCharge, 0f, 1f);
		var px = TextureAssets.MagicPixel.Value;
		float iconHalf = 16f * scale, barW = 26f * scale, barH = 3f * scale;
		float left = center.X - barW * 0.5f;
		float top = center.Y + iconHalf - barH - 2f * scale;
		var bg = new Rectangle((int)left, (int)top, (int)barW, (int)barH);
		sb.Draw(px, bg, Color.Black * 0.7f);
		var col = pct < 0.5f
			? Color.Lerp(Color.Red, Color.Yellow, pct * 2f)
			: Color.Lerp(Color.Yellow, Color.LimeGreen, (pct - 0.5f) * 2f);
		sb.Draw(px, new Rectangle(bg.X, bg.Y, (int)(barW * pct), bg.Height), col);
	}

	private string? ToolDescription()
	{
		if (_type == null) return null;
		string n = _type.Name;
		int aoeLen = 2 * Math.Max(AoeColumn, AoeRow) + 1;

		switch (n)
		{
			case "shovel":         return "Quickly digs soft ground (~3x pickaxe speed). Cannot break stone.";
			case "spade":          return $"Digs a {aoeLen}-tile line of soft ground (~2x pickaxe speed). Cannot break stone.";
			case "pickaxe":        return "Mines blocks and ore.";
			case "mining_hammer":  return $"Mines a {aoeLen}-tile line of blocks.";
			case "axe":            return "Chops trees.";
			case "saw":            return "Chops trees - logs come out as Rubber Wood.";
			case "buzzsaw":        return "Powered saw - chops trees into Rubber Wood.";
			case "hammer":         return "Smashes walls and shapes blocks.";
			case "sword":          return "Melee weapon with slightly extended reach.";
			case "knife":          return "Fast auto-swinging melee weapon.";
			case "crowbar":        return "Heavy melee weapon with strong knockback.";
			case "scythe":         return "Wide auto-swinging weapon. Collects Vine while cutting plants.";
			case "butchery_knife": return "Thrown like a knife and never consumed.";
			case "screwdriver":    return "Pokes like a spear. Right-click a transformer/machine to reconfigure it.";
			case "file":           return "Pokes like a spear (0.75x a screwdriver's power). Crafting tool.";
			case "mortar":         return "Weak pickaxe - mined stone becomes Silt, dirt becomes Sand. Crafting tool.";
			case "wrench":         return "Dismantles machines and cuts item/fluid pipes.";
			case "mallet":         return "Pauses or resumes a machine.";
			case "wire_cutter":    return "Cuts cables and laser pipes.";
			case "plunger":        return $"Absorbs liquid in a 3x3 area. Cooldown: {_useTime} ticks.";
			case "hoe":
			{
				int seedCap = Math.Max(1, (int)Math.Round(5.0 * _tier / 2.0));
				return _tier >= 2
					? $"Harvests herbs for up to {seedCap} bonus seeds and an extra herb."
					: $"Harvests herbs for up to {seedCap} bonus seeds.";
			}
		}

		// Electric tool families (per-tier id like "lv_drill", ...).
		if (n.Contains("drill"))      return $"Powered drill - mines a {aoeLen}-tile line.";
		if (n.Contains("chainsaw"))   return $"Powered chainsaw - chops a {aoeLen}-tile line of trees.";
		if (n.Contains("wirecutter")) return "Powered wire cutter - cuts cables and laser pipes.";
		if (n.EndsWith("wrench"))     return "Powered wrench - dismantles machines and cuts pipes.";
		if (n.Contains("screwdriver"))return "Powered screwdriver - right-click a transformer/machine to reconfigure it.";
		return null;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		if (_type == null) return;
		string? desc = ToolDescription();
		if (desc != null)
			tooltips.Add(new TooltipLine(Mod, "GTToolDesc", desc)
				{ OverrideColor = new Color(170, 210, 235) });
		if (IsKnife)
			tooltips.Add(new TooltipLine(Mod, "GTFriendlyFire", "Can harm friendly NPCs.")
				{ OverrideColor = new Color(220, 60, 60) });
		tooltips.Add(new TooltipLine(Mod, "GTHarvestLevel", $"Harvest level: {_harvestLevel}"));
		if (_type.Definition.SuitableForBlockBreaking)
			tooltips.Add(new TooltipLine(Mod, "GTMiningSpeed", $"Mining speed: {_toolSpeed:0.#}"));
		if (IsElectric)
		{
			float pct = _maxCharge > 0 ? (float)_storedEu / _maxCharge * 100 : 0;
			tooltips.Add(new TooltipLine(Mod, "GTCharge", $"{_storedEu:N0} / {_maxCharge:N0} EU  ({pct:F0}%)"));
		}
	}
}
