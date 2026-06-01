#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Batteries;

// One Terraria ItemID per upstream battery id (lv_lithium_battery, max_battery,
// ...). Mirrors ComponentItem + ElectricStats composition. Configured by ctor
// from the registry dump (BatteryItemLoader): id/label/tier/capacity all come
// from upstream's `ElectricStats` verbatim. Per-stack EU via Clone+Save/Load.
//
// Texture: Content/Textures/item/<id>/{1..8}.png - frame 1 empty -> frame 8 full;
// single-frame batteries ship a flat <id>.png (loader detects via file probe).
public sealed class BatteryItem : ModItem, IElectricItem, ITextureWarmUp
{
	private readonly string? _id;
	private readonly string? _label;
	private readonly VoltageTier _tier;
	private readonly long _maxEu;
	private readonly bool _chargeable;
	private readonly bool _dischargeable;
	private readonly bool _chargeAnimated;

	private long _storedEu;

	public BatteryItem() { }
	public BatteryItem(string id, string label, VoltageTier tier, long maxEu,
		bool chargeable, bool dischargeable, bool chargeAnimated)
	{
		_id = id;
		_label = label;
		_tier = tier;
		_maxEu = maxEu;
		_chargeable = chargeable;
		_dischargeable = dischargeable;
		_chargeAnimated = chargeAnimated;
	}

	public override bool IsLoadingEnabled(Mod mod) => _id != null;
	public override string Name => _id ?? nameof(BatteryItem);
	protected override bool CloneNewInstances => true;

	// upstream ElectricStats.{tier, maxCharge, chargeable, dischargeable}
	public VoltageTier Tier => _tier;
	public long MaxEu => _maxEu;
	public bool IsChargeable    => _chargeable;
	public bool IsDischargeable => _dischargeable;

	// Every shipped upstream battery uses transferLimit = V[tier].
	public long TransferLimit => VoltageTiers.Voltage(_tier);

	public override string Texture => _id == null
		? "Terraria/Images/Item_22"
		: _chargeAnimated
			? $"GregTechCEuTerraria/Content/Textures/item/{_id}/1"
			: $"GregTechCEuTerraria/Content/Textures/item/{_id}";

	public long StoredEu
	{
		get => _storedEu;
		set => _storedEu = System.Math.Clamp(value, 0, _maxEu);
	}

	// IElectricItem - verbatim port of api.item.capability.ElectricItem. Methods
	// intentionally NOT clamped via StoredEu setter - upstream clamps via
	// Math.min(amount, canReceive); bookkeeping matches line-for-line.

	public bool CanProvideChargeExternally() => _dischargeable;
	public bool Chargeable() => _chargeable;
	public long GetTransferLimit() => TransferLimit;
	public long GetMaxCharge() => _maxEu;
	public long GetCharge() => _storedEu;
	public int  GetTier() => (int)_tier;

	public long Charge(long amount, int chargerTier, bool ignoreTransferLimit, bool simulate)
	{
		if (Item.stack != 1) return 0L;
		int tier = (int)_tier;
		if ((_chargeable || amount == long.MaxValue) && (chargerTier >= tier) && amount > 0L)
		{
			long canReceive = _maxEu - _storedEu;
			if (!ignoreTransferLimit)
				amount = System.Math.Min(amount, TransferLimit);
			long charged = System.Math.Min(amount, canReceive);
			if (!simulate)
				_storedEu += charged;
			return charged;
		}
		return 0;
	}

	public long Discharge(long amount, int chargerTier, bool ignoreTransferLimit, bool externally, bool simulate)
	{
		if (Item.stack != 1) return 0L;
		int tier = (int)_tier;
		if ((_dischargeable || !externally || amount == long.MaxValue) && (chargerTier >= tier) && amount > 0L)
		{
			if (!ignoreTransferLimit)
				amount = System.Math.Min(amount, TransferLimit);
			long charge = _storedEu;
			long discharged = System.Math.Min(amount, charge);
			if (!simulate)
				_storedEu = charge - discharged;
			return discharged;
		}
		return 0;
	}

	public override void SetStaticDefaults()
	{
		base.SetStaticDefaults();
		if (_label != null)
			Terraria.Localization.Language.GetOrRegister(
				$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName", () => _label);
	}

	public override void SetDefaults()
	{
		Item.maxStack = 1;
		Item.width = 32;
		Item.height = 32;
		Item.rare = ItemRarityID.White;
	}

	// Default Clone doesn't carry our private fields - override so stack-clone
	// paths preserve per-stack charge.
	public override ModItem Clone(Item newEntity)
	{
		var clone = (BatteryItem)base.Clone(newEntity);
		clone._storedEu = _storedEu;
		return clone;
	}

	public override void SaveData(TagCompound tag) => tag["eu"] = _storedEu;
	public override void LoadData(TagCompound tag) => _storedEu = tag.ContainsKey("eu") ? tag.GetLong("eu") : 0;
	public override void NetSend(System.IO.BinaryWriter writer) => writer.Write(_storedEu);
	public override void NetReceive(System.IO.BinaryReader reader) => _storedEu = reader.ReadInt64();

	// State-dependent: animated batteries swap through 8 frames per charge level
	// for inventory + world; ItemIconBaker bakes frame 1 into TextureAssets.Item
	// so the held-item draw path (no PreDraw hook) gets a real upscaled icon.

	private const float ItemRenderScale = 2f;

	public override void HoldItem(Player player)
	{
		base.HoldItem(player);
		ItemIconBaker.Install(Item.type, Texture);
	}

	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type, Texture);

	private string CurrentChargeFrameTexture()
	{
		if (!_chargeAnimated) return Texture;
		float pct = _maxEu > 0 ? (float)_storedEu / _maxEu : 0f;
		int frame = _storedEu <= 0 ? 1 : 1 + (int)System.Math.Floor(System.Math.Clamp(pct, 0f, 1f) * 7f);
		return $"GregTechCEuTerraria/Content/Textures/item/{_id}/{frame}";
	}

	public override bool PreDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		var tex = ModContent.Request<Texture2D>(CurrentChargeFrameTexture()).Value;
		// Vanilla's origin/frame are sized for the 2x baked frame-1 in
		// TextureAssets.Item; we're drawing the raw 16x16 per-charge source so
		// recompute both against `tex` (else origin=(16,16) on a 16x16 source
		// pushes the icon off-center).
		var srcFrame = tex.Frame();
		var srcOrigin = srcFrame.Size() * 0.5f;
		float drawScale = scale * ItemRenderScale;
		TerrariaCompat.UI.PointClampDraw.Draw(sb, () =>
			sb.Draw(tex, position, srcFrame, drawColor, 0f, srcOrigin, drawScale, SpriteEffects.None, 0f));
		return false;
	}

	public override bool PreDrawInWorld(SpriteBatch sb, Color lightColor, Color alphaColor, ref float rotation, ref float scale, int whoAmI)
	{
		var tex = ModContent.Request<Texture2D>(CurrentChargeFrameTexture()).Value;
		var srcFrame = tex.Frame();
		var origin = srcFrame.Size() * 0.5f;
		var pos = Item.Center - Main.screenPosition;
		float drawScale = scale * ItemRenderScale;
		float drawRot = rotation;
		TerrariaCompat.UI.PointClampDraw.Draw(sb, () =>
			sb.Draw(tex, pos, srcFrame, lightColor, drawRot, origin, drawScale, SpriteEffects.None, 0f));
		return false;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		tooltips.ApplyTierColor(_tier);
		float pct = _maxEu > 0 ? (float)_storedEu / _maxEu * 100 : 0;
		tooltips.Add(new TooltipLine(Mod, "BatteryStored",
			$"{_storedEu:N0} / {_maxEu:N0} EU  ({pct:F0}%)"));
		tooltips.Add(new TooltipLine(Mod, "BatteryTier",
			$"{VoltageTiers.ShortName(_tier)} - {VoltageTiers.Voltage(_tier):N0} EU/t max"));
	}

	public override void PostDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		base.PostDrawInInventory(spriteBatch, position, frame, drawColor, itemColor, origin, scale);
		// `position` is the icon CENTER (vanilla uses origin = frame.Size()/2).
		DrawChargeBar(spriteBatch, position, scale, isInventory: true);
	}

	public override void PostDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor, float rotation, float scale, int whoAmI)
	{
		base.PostDrawInWorld(spriteBatch, lightColor, alphaColor, rotation, scale, whoAmI);
		var center = Item.Center - Main.screenPosition;
		DrawChargeBar(spriteBatch, center, scale, isInventory: false);
	}

	private void DrawChargeBar(SpriteBatch sb, Vector2 center, float scale, bool isInventory)
	{
		if (_maxEu <= 0) return;
		float pct = System.Math.Clamp((float)_storedEu / _maxEu, 0f, 1f);
		var px = TextureAssets.MagicPixel.Value;

		float u = ItemRenderScale * scale; // 1 source-pixel = this many screen pixels
		float iconHalf = 8f * u;
		float barW     = 14f * u;
		float barH     = 1f  * u;
		float left     = center.X - barW * 0.5f;
		float top      = center.Y + iconHalf - barH - u;

		var bg = new Rectangle((int)left, (int)top, (int)barW, (int)barH);
		sb.Draw(px, bg, Color.Black * 0.7f);
		int fillW = (int)(barW * pct);
		var col = pct < 0.5f
			? Color.Lerp(Color.Red,    Color.Yellow, pct * 2f)
			: Color.Lerp(Color.Yellow, Color.LimeGreen, (pct - 0.5f) * 2f);
		sb.Draw(px, new Rectangle(bg.X, bg.Y, fillW, bg.Height), col);
	}
}
