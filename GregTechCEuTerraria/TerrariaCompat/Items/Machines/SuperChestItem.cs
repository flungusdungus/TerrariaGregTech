#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Machines;

// Stored item (in MachinePortableData, stamped by GetItemDrops) is drawn on the
// icon and named in the tooltip. Item-side mirror of SuperTankItem.
public class SuperChestItem : TieredMachineItem
{
	public SuperChestItem() { }
	public SuperChestItem(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	private (int type, long amount) Stored()
	{
		if (Item.TryGetGlobalItem<MachinePortableData>(out var g)
		    && g.Data is { } d && d.ContainsKey("stored"))
		{
			var it = ItemIO.Load(d.GetCompound("stored"));
			if (!it.IsAir) return (it.type, d.GetLong("storedAmount"));
		}
		return (0, 0);
	}

	protected override void AppendTierTooltip(List<TooltipLine> tooltips)
	{
		long cap = SuperChestTileEntity.MaxAmountForTier(_tier);
		string capStr = cap == long.MaxValue ? "~9.2E (cap)" : $"{cap:N0}";
		tooltips.Add(new TooltipLine(Mod, "TierLine",
			$"{VoltageTiers.ShortName(_tier)} - capacity {capStr} items"));
		var (type, amount) = Stored();
		if (type > 0)
			tooltips.Add(new TooltipLine(Mod, "ChestContents",
				$"Contains {amount:N0} x {Lang.GetItemNameValue(type)}"));
	}

	// Stored-item overlay - mirrors SuperChestTile.PostDraw.
	public override void PostDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame,
		Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		base.PostDrawInInventory(sb, position, frame, drawColor, itemColor, origin, scale);
		var (type, _) = Stored();
		if (type > 0) DrawStored(sb, type, position, (int)(16f * scale), drawColor);
	}

	public override void PostDrawInWorld(SpriteBatch sb, Color lightColor, Color alphaColor,
		float rotation, float scale, int whoAmI)
	{
		base.PostDrawInWorld(sb, lightColor, alphaColor, rotation, scale, whoAmI);
		var (type, _) = Stored();
		if (type > 0) DrawStored(sb, type, Item.Center - Main.screenPosition, (int)(16f * scale), lightColor);
	}

	private static void DrawStored(SpriteBatch sb, int type, Vector2 center, int size, Color light)
	{
		Main.instance.LoadItem(type);
		Main.GetItemDrawFrame(type, out var tex, out var srcFrame);
		if (tex is null) return;
		var box = new Rectangle((int)(center.X - size / 2f), (int)(center.Y - size / 2f), size, size);
		sb.Draw(tex, box, srcFrame, light);
	}
}
