#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Machines;

// Stored fluid (in MachinePortableData, stamped by GetItemDrops) is drawn on
// the icon and named in the tooltip. Same pattern as the tile.
public class SuperTankItem : TieredMachineItem
{
	public SuperTankItem() { }
	public SuperTankItem(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	private FluidStack StoredFluid()
	{
		if (Item.TryGetGlobalItem<MachinePortableData>(out var g)
		    && g.Data is { } d && d.ContainsKey("fluidId")
		    && FluidRegistry.TryGet(d.GetString("fluidId"), out var type))
			// FluidStack amount is int; high-tier tanks exceed it - clamp.
			return new FluidStack(type, (int)System.Math.Min(d.GetLong("fluidAmount"), int.MaxValue));
		return FluidStack.Empty;
	}

	// Capacity = 4096 x 4^tier, saturating at int.MaxValue.
	protected override void AppendTierTooltip(List<TooltipLine> tooltips)
	{
		long cap = 4096L * (1L << (2 * (int)_tier));
		string capStr = cap > int.MaxValue ? "~2.1G (cap)" : $"{cap:N0}";
		tooltips.Add(new TooltipLine(Mod, "TierLine",
			$"{VoltageTiers.ShortName(_tier)} - capacity {capStr} mB"));
		var stored = StoredFluid();
		if (!stored.IsEmpty)
			tooltips.Add(new TooltipLine(Mod, "TankContents",
				$"Contains {stored.Amount:N0} mB of {stored.Type!.DisplayName}"));
	}

	// Stored-fluid overlay (centre 50% inset) - mirrors SuperTankTile.PostDraw.
	public override void PostDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame,
		Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		base.PostDrawInInventory(sb, position, frame, drawColor, itemColor, origin, scale);
		var stored = StoredFluid();
		if (stored.IsEmpty) return;
		int inner = (int)(16f * scale);
		var dest = new Rectangle(
			(int)(position.X - inner / 2f), (int)(position.Y - inner / 2f), inner, inner);
		FluidIconRenderer.Draw(sb, stored.Type!, dest, light: drawColor);
	}

	public override void PostDrawInWorld(SpriteBatch sb, Color lightColor, Color alphaColor,
		float rotation, float scale, int whoAmI)
	{
		base.PostDrawInWorld(sb, lightColor, alphaColor, rotation, scale, whoAmI);
		var stored = StoredFluid();
		if (stored.IsEmpty) return;
		var center = Item.Center - Main.screenPosition;
		int inner = (int)(16f * scale);
		var dest = new Rectangle(
			(int)(center.X - inner / 2f), (int)(center.Y - inner / 2f), inner, inner);
		FluidIconRenderer.Draw(sb, stored.Type!, dest, light: lightColor);
	}
}
