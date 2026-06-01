#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Items.Cables;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Cursor-hover tooltip for machines. PostUpdate path shared with WireItem.
// Suppressed while holding a wire item (cable layer owns that hover).
// Tooltip composes up the MetaMachine inheritance chain via base.AppendTooltip.
public sealed class MachineHoverModPlayer : ModPlayer
{
	private readonly List<string> _buffer = new();

	public override void PostUpdate()
	{
		if (Player.whoAmI != Main.myPlayer) return;
		if (Player.mouseInterface || Main.gameMenu) return;
		if (Player.HeldItem?.ModItem is WireItem) return;

		int x = (int)(Main.MouseWorld.X / 16);
		int y = (int)(Main.MouseWorld.Y / 16);
		if (x <= 0 || x >= Main.maxTilesX - 1 || y <= 0 || y >= Main.maxTilesY - 1) return;

		// Multi-tile-aware: any sub-cell resolves to the entity at top-left.
		if (MachineCellResolver.TryFindMachineAt(x, y, out var machine))
		{
			_buffer.Clear();
			machine.AppendTooltip(_buffer);
			if (_buffer.Count == 0) return;

			// Route via WorldHoverTooltip - direct MouseText leaks through UI
			// because PostUpdate fires before Player.mouseInterface is set.
			TerrariaCompat.UI.WorldHoverTooltip.Set(string.Join("\n", _buffer));
			return;
		}

		// Unformed multi ghost-preview hover - shows the cell's predicate.
		if (MultiblockPreviewHover.TryFind(x, y, out var controller, out _, out var predicate))
		{
			_buffer.Clear();
			MultiblockPreviewHover.AppendTooltip(_buffer, controller, predicate, x, y);
			if (_buffer.Count == 0) return;
			TerrariaCompat.UI.WorldHoverTooltip.Set(string.Join("\n", _buffer));
		}
	}
}
