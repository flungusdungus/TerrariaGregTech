#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Items.Cables;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.Common;

// Holding a WireItem must not also open the chest/machine under the cursor.
// mouseInterface / controlUseTile don't gate TileInteractionsUse on every
// dispatcher, so short-circuit the player-level entry directly.
public sealed class WireHeldTileInteractSuppressor : ModSystem
{
	public override void Load()
	{
		On_Player.TileInteractionsUse += SuppressIfWireHeld;
	}

	public override void Unload()
	{
		On_Player.TileInteractionsUse -= SuppressIfWireHeld;
	}

	private static void SuppressIfWireHeld(On_Player.orig_TileInteractionsUse orig, Player self, int myX, int myY)
	{
		if (self.HeldItem?.ModItem is WireItem)
			return;
		orig(self, myX, myY);
	}
}
