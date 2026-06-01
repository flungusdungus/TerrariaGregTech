#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// Base for every gtceu cell item. Mirrors MetaItem ComponentItem +
// ItemFluidContainer - one ItemID per cell tier, contents in NBT
// ("fluid" -> { id, amount }; absent / amount=0 = empty).
//
// Subclasses override SnakeName/Label/CellMaterialColor/Capacity/MaxStack.
// Stacking: only empty cells stack; filled cells unique per slot.
public abstract class FluidCellItem : ModItem, IFluidHandlerItem, ITextureWarmUp
{
	protected abstract string SnakeName { get; }       // upstream id ("fluid_cell")
	protected abstract string Label { get; }            // "Empty Cell" / ...
	protected virtual uint CellMaterialColor => 0xFFFFFFFF;
	public abstract int Capacity { get; }              // mB
	protected virtual int CellMaxStack => 99;
	public override string Texture => $"GregTechCEuTerraria/Content/Textures/item/{SnakeName}/base";

	public override string Name => SnakeName;

	public override void SetStaticDefaults()
	{
		base.SetStaticDefaults();
		// Default; per-stack title is set in ModifyTooltips (Terraria has no
		// per-instance HoverName, so we replace the ItemName line).
		Terraria.Localization.Language.GetOrRegister($"Mods.GregTechCEuTerraria.Items.{SnakeName}.DisplayName",
			() => Label);
	}

	public override void SetDefaults()
	{
		Item.maxStack = CellMaxStack;
		Item.width = 32;
		Item.height = 32;
		Item.rare  = ItemRarityID.White;
		Item.consumable = false;
	}

	public FluidStack GetFluidStack()
	{
		var tag = Item.ModItem is FluidCellItem c ? c._fluidTag : null;
		if (tag is null) return FluidStack.Empty;
		string id = tag.GetString("id");
		int amount = tag.GetInt("amount");
		if (string.IsNullOrEmpty(id) || amount <= 0) return FluidStack.Empty;
		if (!FluidRegistry.TryGet(id, out var type)) return FluidStack.Empty;
		return new FluidStack(type, amount);
	}

	private void SetFluidStack(FluidStack stack)
	{
		if (stack.IsEmpty)
		{
			_fluidTag = null;
			return;
		}
		_fluidTag = new TagCompound
		{
			["id"]     = stack.Type!.Id,
			["amount"] = stack.Amount,
		};
	}

	// Per-stack - Terraria gives each Item its own ModItem instance.
	private TagCompound? _fluidTag;
	protected override bool CloneNewInstances => false;

	public override void SaveData(TagCompound tag)
	{
		if (_fluidTag is not null) tag["fluid"] = _fluidTag;
	}

	public override void LoadData(TagCompound tag)
	{
		_fluidTag = tag.GetCompound("fluid");
		if (_fluidTag is { Count: 0 }) _fluidTag = null;
	}

	public override ModItem Clone(Item newEntity)
	{
		var c = (FluidCellItem)base.Clone(newEntity);
		c._fluidTag = _fluidTag is null ? null : (TagCompound)_fluidTag.Clone();
		return c;
	}

	public Item Container => Item;
	public int TankCount => 1;
	public int GetCapacity(int tank) => Capacity;
	public FluidStack GetTank(int tank) => GetFluidStack();

	public bool IsFluidValid(int tank, FluidStack fluid) => true;

	// Single-tank: whole-handler Fill/Drain ARE the tank-0 methods.
	public int Fill(FluidStack fluid, bool simulate)
	{
		if (fluid.IsEmpty) return 0;
		var existing = GetFluidStack();
		if (!existing.IsEmpty && !existing.SameTypeAs(fluid)) return 0;
		int room = Capacity - existing.Amount;
		if (room <= 0) return 0;
		int accepted = System.Math.Min(room, fluid.Amount);
		if (!simulate)
			SetFluidStack(existing.IsEmpty
				? new FluidStack(fluid.Type!, accepted, fluid.Nbt)
				: existing.WithAmount(existing.Amount + accepted));
		return accepted;
	}

	public FluidStack Drain(int maxAmount, bool simulate)
	{
		var existing = GetFluidStack();
		if (existing.IsEmpty || maxAmount <= 0) return FluidStack.Empty;
		int drained = System.Math.Min(existing.Amount, maxAmount);
		var result = new FluidStack(existing.Type!, drained, existing.Nbt);
		if (!simulate)
			SetFluidStack(drained == existing.Amount
				? FluidStack.Empty
				: existing.WithAmount(existing.Amount - drained));
		return result;
	}

	public FluidStack Drain(FluidStack fluidStack, bool simulate)
	{
		var existing = GetFluidStack();
		if (existing.IsEmpty || !existing.SameTypeAs(fluidStack)) return FluidStack.Empty;
		return Drain(fluidStack.Amount, simulate);
	}

	public override bool CanStack(Item item2)
	{
		if (item2.ModItem is not FluidCellItem other) return false;
		return GetFluidStack().IsEmpty && other.GetFluidStack().IsEmpty;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		var stack = GetFluidStack();
		string title = stack.IsEmpty
			? $"Empty {Label}"
			: $"{Label} of {stack.Type!.DisplayName}";
		int nameIdx = tooltips.FindIndex(t => t.Name == "ItemName");
		if (nameIdx >= 0) tooltips[nameIdx].Text = title;

		if (!stack.IsEmpty)
		{
			tooltips.Add(new TooltipLine(Mod, "FluidAmount",
				$"{stack.Amount:N0} / {Capacity:N0} mB"));
		}
		else
		{
			tooltips.Add(new TooltipLine(Mod, "FluidCapacity",
				$"Capacity: {Capacity:N0} mB"));
		}
	}

	// State-dependent: per-stack fluid overlay can't bake into the type-keyed
	// TextureAssets.Item, so PreDraw composites both layers for inventory +
	// world drop. ItemIconBaker bakes the empty-cell base into TextureAssets
	// for the held-item path (which has no PreDraw hook); contents show only
	// in inventory + world drop.

	private const float ItemRenderScale = 2f;

	private Asset<Texture2D>? _baseTex;
	private Asset<Texture2D>? _overlayTex;

	public override void HoldItem(Player player)
	{
		base.HoldItem(player);
		EnsureTextureBaked();
	}

	void ITextureWarmUp.WarmUpTexture() => EnsureTextureBaked();

	private void EnsureTextureBaked()
	{
		uint argb = CellMaterialColor;
		var tint = new Color(
			(byte)((argb >> 16) & 0xFF),
			(byte)((argb >> 8) & 0xFF),
			(byte)(argb & 0xFF));
		ItemIconBaker.Install(Item.type, Texture, tint);
	}

	public override bool PreDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame,
		Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		// Recompute origin/frame against the raw 16x16 source - see BatteryItem.
		_baseTex ??= ModContent.Request<Texture2D>(Texture);
		var srcFrame = _baseTex?.Value is { } bt ? bt.Frame() : frame;
		var srcOrigin = srcFrame.Size() * 0.5f;
		scale *= ItemRenderScale;
		TerrariaCompat.UI.PointClampDraw.Draw(sb, () => DrawLayers(sb, position, srcFrame, drawColor, srcOrigin, scale));
		return false;
	}

	public override bool PreDrawInWorld(SpriteBatch sb, Color lightColor, Color alphaColor,
		ref float rotation, ref float scale, int whoAmI)
	{
		scale *= ItemRenderScale;
		var tex = TextureAssets.Item[Item.type].Value;
		var frame = Main.itemAnimations[Item.type]?.GetFrame(tex) ?? tex.Frame();
		var origin = frame.Size() * 0.5f;
		var pos = Item.Center - Main.screenPosition;
		float drawScale = scale;
		float drawRot = rotation;
		TerrariaCompat.UI.PointClampDraw.Draw(sb, () => DrawLayers(sb, pos, frame, lightColor, origin, drawScale, drawRot));
		return false;
	}

	// Two-layer: base tinted by CellMaterialColor; fluid overlay tinted by
	// fluid color, animated (16x64 = 4-frame strip, frametime 10 ticks).
	// Frame snap, no interp. mcmeta frametime is hardcoded; read it from JSON
	// at load if upstream ever ships non-standard timing.
	private const int OverlayFrameTicks = 10;

	private void DrawLayers(SpriteBatch sb, Vector2 position, Rectangle frame,
		Color drawColor, Vector2 origin, float scale, float rotation = 0f)
	{
		_baseTex    ??= ModContent.Request<Texture2D>(Texture);
		_overlayTex ??= ModContent.Request<Texture2D>(
			$"GregTechCEuTerraria/Content/Textures/item/{SnakeName}/overlay");

		if (_baseTex?.Value is { } baseTex)
		{
			var cellTint = MultiplyColor(drawColor, CellMaterialColor);
			sb.Draw(baseTex, position, frame, cellTint, rotation, origin, scale, SpriteEffects.None, 0f);
		}

		var fluid = GetFluidStack();
		if (!fluid.IsEmpty && _overlayTex?.Value is { } overlay)
		{
			var overlaySrc = CurrentOverlayFrame(overlay);
			var fluidTint = MultiplyColor(drawColor, fluid.Type!.Color);
			sb.Draw(overlay, position, overlaySrc, fluidTint, rotation, origin, scale, SpriteEffects.None, 0f);
		}
	}

	private static Rectangle CurrentOverlayFrame(Texture2D sheet)
	{
		int frameH = sheet.Width; // square frames
		int frameCount = frameH > 0 ? sheet.Height / frameH : 1;
		if (frameCount <= 1) return new Rectangle(0, 0, sheet.Width, sheet.Height);
		int frameIdx = (int)(Main.GameUpdateCount / OverlayFrameTicks) % frameCount;
		return new Rectangle(0, frameIdx * frameH, sheet.Width, frameH);
	}

	private static Color MultiplyColor(Color baseColor, uint argb)
	{
		byte r = (byte)((argb >> 16) & 0xFF);
		byte g = (byte)((argb >> 8) & 0xFF);
		byte b = (byte)(argb & 0xFF);
		return new Color(
			baseColor.R * r / 255,
			baseColor.G * g / 255,
			baseColor.B * b / 255,
			baseColor.A);
	}
}
