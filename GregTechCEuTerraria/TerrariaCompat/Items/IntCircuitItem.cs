#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Port of upstream `programmed_circuit` (ComponentItem +
// IntCircuitBehaviour). "Configuration" NBT 0..32, key verbatim with upstream.
// Upstream uses a 33-button held GUI; we cycle +1 on inventory RMB (cellphone
// idiom), wrapping 32 -> 0, not consumed. The item is redundant in normal play
// (machines match against the UICircuitButton ghost slot) but kept for parity.
public sealed class IntCircuitItem : ModItem, ITextureWarmUp
{
	public const int CircuitMin = 0;
	public const int CircuitMax = 32;          // upstream IntCircuitBehaviour.CIRCUIT_MAX

	public override string Name => "programmed_circuit";

	// Upstream off-by-one: model `<N>.json` references `<N+1>.png` (files 1..33,
	// digits 0..32 baked in). PreDraw swaps in the live config sprite.
	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/programmed_circuit/1";

	private int _configuration;
	public int Configuration
	{
		get => _configuration;
		set => _configuration = Math.Clamp(value, CircuitMin, CircuitMax);
	}

	public override void SetStaticDefaults()
	{
		// port-locale.py is dump-driven for plain-Item entries only; register
		// programmed_circuit's display name at runtime.
		Terraria.Localization.Language.GetOrRegister(
			"Mods.GregTechCEuTerraria.Items.programmed_circuit.DisplayName",
			() => "Programmed Circuit");
	}

	public override void SetDefaults()
	{
		Item.maxStack = 1;
		Item.width = 32;
		Item.height = 32;
		Item.value = Terraria.Item.buyPrice(silver: 2);
		Item.rare = ItemRarityID.White;
		Item.consumable = false;
	}

	public override bool CanRightClick() => true;

	public override void RightClick(Player player)
	{
		Configuration = _configuration >= CircuitMax ? CircuitMin : _configuration + 1;
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	public override bool ConsumeItem(Player player) => false;       // keep on RMB

	public override void SaveData(TagCompound tag) => tag["Configuration"] = _configuration;

	public override void LoadData(TagCompound tag) =>
		Configuration = tag.GetInt("Configuration");

	public override void NetSend(BinaryWriter writer) => writer.Write((byte)_configuration);
	public override void NetReceive(BinaryReader reader) => Configuration = reader.ReadByte();

	public override bool CanStack(Item source) =>
		source.ModItem is IntCircuitItem other && other._configuration == _configuration;

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		tooltips.Add(new TooltipLine(Mod, "Configuration", $"Configuration: {_configuration}"));
	}

	// State-dep per-stack render: PreDraw picks the per-config sprite for
	// inventory + world drop. ItemIconBaker bakes the config-0 sprite into
	// TextureAssets.Item for the held-item path; swing shows config-0 (circuits
	// aren't swung in practice so this fallback is invisible).

	private const float ItemRenderScale = 2f;

	// Shared across all per-stack instances - keyed by config value.
	private static readonly Asset<Texture2D>?[] _spriteByValue = new Asset<Texture2D>?[CircuitMax + 1];

	private static Texture2D? Sprite(int value)
	{
		if (value < CircuitMin || value > CircuitMax) return null;
		int fileIndex = value + 1;
		_spriteByValue[value] ??= ModContent.Request<Texture2D>(
			$"GregTechCEuTerraria/Content/Textures/item/programmed_circuit/{fileIndex}");
		return _spriteByValue[value]?.Value;
	}

	public override void HoldItem(Player player)
	{
		base.HoldItem(player);
		ItemIconBaker.Install(Item.type, Texture);
	}

	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type, Texture);

	public override bool PreDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame,
		Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		var tex = Sprite(_configuration) ?? TextureAssets.Item[Item.type].Value;
		// Recompute origin/frame from `tex` (raw 16x16) - see BatteryItem.
		var srcFrame = tex.Frame();
		var srcOrigin = srcFrame.Size() * 0.5f;
		float drawScale = scale * ItemRenderScale;
		UI.PointClampDraw.Draw(sb, () =>
			sb.Draw(tex, position, srcFrame, drawColor, 0f, srcOrigin, drawScale, SpriteEffects.None, 0f));
		return false;
	}

	public override bool PreDrawInWorld(SpriteBatch sb, Color lightColor, Color alphaColor,
		ref float rotation, ref float scale, int whoAmI)
	{
		var tex = Sprite(_configuration) ?? TextureAssets.Item[Item.type].Value;
		var frame = tex.Frame();
		var origin = frame.Size() * 0.5f;
		var pos = Item.Center - Main.screenPosition;
		float drawScale = scale * ItemRenderScale;
		float drawRot = rotation;
		UI.PointClampDraw.Draw(sb, () =>
			sb.Draw(tex, pos, frame, lightColor, drawRot, origin, drawScale, SpriteEffects.None, 0f));
		return false;
	}
}
