#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Graphics;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// Ultimate weapon - one per material with a full non-electric toolset.
// Zenith clone: spawns ProjectileID.FinalFractal (vanilla AI 182) on each swing.
// Damage/useTime sit BELOW the bare-material sword so the multi-hit fractal AI
// is the gimmick rather than raw burst damage.
public sealed class GregithItem : ModItem
{
	private readonly string? _id;
	private readonly string? _label;
	[CloneByReference] private readonly Material? _material;
	private readonly int _tier;
	[CloneByReference] private readonly int[] _ingredientItemTypes = Array.Empty<int>();
	// Recipe consumes _ingredientItemTypes for a normal Gregith; the Overclocked
	// variant consumes one of every other Gregith while still cycling the
	// pooled tool icons.
	[CloneByReference] private readonly int[] _recipeItemTypes = Array.Empty<int>();
	// Overclocked = combine-all-Gregiths endgame. 2x damage of Neutronium
	// Gregith, 4 fractals per swing.
	private readonly bool _overclocked;
	public IReadOnlyList<int> IngredientItemTypes => _ingredientItemTypes;

	private const int OverclockedShotsPerSwing = 4;

	public GregithItem() { }

	public GregithItem(string id, string label, Material material, int tier, int[] ingredients,
		bool overclocked = false, int[]? recipeItemTypes = null)
	{
		_id = id;
		_label = label;
		_material = material;
		_tier = tier;
		_ingredientItemTypes = ingredients;
		_recipeItemTypes = recipeItemTypes ?? ingredients;
		_overclocked = overclocked;
	}

	public override bool IsLoadingEnabled(Mod mod) => _id != null;
	public override string Name => _id ?? nameof(GregithItem);
	// Autoload fallback; actual icon cycles ingredients via PreDraw.
	public override string Texture => "Terraria/Images/Item_4956";
	protected override bool CloneNewInstances => true;

	// Tier-cap on AI_182's orbit reach (orbit radius ~ velocity.Length()+40).
	// Vanilla Zenith is uncapped; we clamp lower tiers progressively so an LV
	// Steel Gregith doesn't sweep a screen-wide rainbow.
	private static readonly float[] MaxOrbitVelocityByTier =
	{
		 40f, // 0 ULV   - bronze / invar
		 80f, // 1 LV    - steel
		140f, // 2 MV    - aluminium
		200f, // 3 HV    - stainless
		280f, // 4 EV    - titanium
		360f, // 5 IV    - tungsten_steel
		440f, // 6 LuV   - hsse
		560f, // 7 ZPM   - duranium
		720f, // 8 UV    - naquadah_alloy
		900f, // 9 UHV+  - neutronium (~ vanilla Zenith full-screen reach)
	};

	public override void SetStaticDefaults()
	{
		if (_label != null)
			Terraria.Localization.Language.GetOrRegister(
				$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName", () => _label);
	}

	public override void SetDefaults()
	{
		var anchor = ToolTier.AnchorFor(_tier);

		Item.maxStack = 1;
		Item.width = Item.height = 32;
		Item.DamageType = DamageClass.Melee;

		// Half-damage per projectile compensates for the multi-hit fractal AI.
		// Overclocked skips the halving (= 2x Neutronium Gregith damage).
		Item.damage = _overclocked ? anchor.Damage : Math.Max(1, anchor.Damage / 2);
		// Slower than bare material sword (compensates the multi-hit AI).
		Item.useTime = Item.useAnimation = anchor.UseTime + 5;
		Item.knockBack = 6.5f;
		Item.shootSpeed = 12f;
		Item.shoot = ProjectileID.FinalFractal;
		Item.UseSound = SoundID.Item1;
		Item.useStyle = ItemUseStyleID.Swing;
		Item.autoReuse = true;
		Item.noMelee = true;        // damage via projectile only
		Item.noUseGraphic = true;   // verbatim vanilla Zenith - projectile is the visual
		Item.rare = ItemRarityID.Red;
		Item.value = Item.sellPrice(gold: 5 + _tier);
	}

	public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position,
		Vector2 velocity, int type, int damage, float knockback)
	{
		// Verbatim Player.cs:48083: ai[0]=spin offset, ai[1]=fractal colour key.
		// Actual sprite is set on the projectile's ToolItemId (ExtraAI-synced).
		// Each Overclocked shot picks its own spin/colour/scatter/ingredient so
		// the swarm looks like 4 independent fractals.
		int shots = _overclocked ? OverclockedShotsPerSwing : 1;
		for (int i = 0; i < shots; i++)
			SpawnFractal(player, source, position, knockback, type, damage);
		return false; // we spawned the projectile(s) ourselves
	}

	private void SpawnFractal(Player player, EntitySource_ItemUse_WithAmmo source,
		Vector2 position, float knockback, int type, int damage)
	{
		int colourKey = FinalFractalHelper.GetRandomProfileIndex();
		float spinOffset = Main.rand.Next(-100, 101);
		// Vanilla FinalFractal - type 933 triggers every hardcoded vanilla path
		// (AI_182 orbit, Main.cs:28080 trail render, Projectile.cs:19027 skip).
		// Sprite swap via GregithProjectileGlobal.PreDraw only.
		//
		// Verbatim vanilla-Zenith velocity (Player.cs:48118-48121):
		// `(MouseWorld - MountedCenter) / 2` with NextVector2Circular(150,150)
		// scatter, then clamped to MaxOrbitVelocityByTier so lower tiers don't
		// match Zenith's full-screen sweep.
		Vector2 mouseWorld = Main.MouseWorld;
		player.LimitPointToPlayerReachableArea(ref mouseWorld);
		mouseWorld += Main.rand.NextVector2Circular(150f, 150f);
		Vector2 zenithVel = (mouseWorld - player.MountedCenter) / 2f;
		float maxLen = MaxOrbitVelocityByTier[Math.Clamp(_tier, 0, MaxOrbitVelocityByTier.Length - 1)];
		if (zenithVel.LengthSquared() > maxLen * maxLen)
			zenithVel = Vector2.Normalize(zenithVel) * maxLen;
		// Stash BEFORE NewProjectile so the synchronous SyncProjectile packet
		// at the end of NewProjectile_Inner carries the id via SendExtraAI;
		// setting it after-the-fact doesn't reliably re-sync ExtraAI for AI_182.
		int toolId = _ingredientItemTypes.Length > 0
			? _ingredientItemTypes[Main.rand.Next(_ingredientItemTypes.Length)] : 0;
		GregithProjectileGlobal.SetPendingToolItemId(toolId);
		Projectile.NewProjectile(source, position, zenithVel, type, damage, knockback,
			player.whoAmI, spinOffset, colourKey);
	}


	public override void AddRecipes()
	{
		if (_recipeItemTypes.Length < 2) return;
		var r = CreateRecipe();
		foreach (int t in _recipeItemTypes) r.AddIngredient(t, 1);
		r.AddTile(TileID.WorkBenches);
		r.Register();
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		if (!_overclocked) return;
		var accent = new Color(255, 170, 60);
		tooltips.Add(new TooltipLine(Mod, "GregithOverclockDamage",
			"Deals double the damage of the Neutronium Gregith") { OverrideColor = accent });
		tooltips.Add(new TooltipLine(Mod, "GregithOverclockTools",
			$"Hurls {OverclockedShotsPerSwing}x the tools per swing") { OverrideColor = accent });
	}

	// 1-Hz slideshow of recipe ingredient tools. We swap the Asset reference
	// in TextureAssets.Item[Item.type] to the current ingredient's baked entry
	// so vanilla draws the Gregith at the same slot scale as the underlying
	// tool (an arbitrary sb.Draw at the passed scale would render at the wrong
	// size - ItemSlot.Draw scales by the texture's dimensions).

	private void RetargetIconToCurrentIngredient()
	{
		if (_ingredientItemTypes.Length == 0) return;
		int idx = (int)(Main.GameUpdateCount / 60u) % _ingredientItemTypes.Length;
		int toolId = _ingredientItemTypes[idx];
		if (toolId <= 0 || toolId >= TextureAssets.Item.Length) return;
		// Bake via ItemLoader.GetItem - ContentSamples stores a clone (because
		// ToolItem sets CloneNewInstances=true) whose _layers is empty, so the
		// bake would no-op and we'd swap to the untinted autoload head layer.
		if (ItemLoader.GetItem(toolId) is ToolItem tool)
			tool.EnsureTextureBaked();
		TextureAssets.Item[Item.type] = TextureAssets.Item[toolId];
	}

	public override bool PreDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame,
		Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		RetargetIconToCurrentIngredient();
		return true;
	}

	public override bool PreDrawInWorld(SpriteBatch sb, Color lightColor, Color alphaColor,
		ref float rotation, ref float scale, int whoAmI)
	{
		RetargetIconToCurrentIngredient();
		return true;
	}
}
