#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Items.Fluids;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Shared click matrix for browser slots (recipe rows, items grid, loot, favs).
//   LMB / RMB      -> Recipes view, "how to obtain" / "used as ingredient"
//   Alt+LMB        -> favorite (or unfavorite inside the favorites pane)
//   Ctrl+LMB / RMB -> Journey cheat: one unit / a stack
// Tag cells only honour LMB/RMB - favorite + cheat fall through (ambiguous set).
public static class BrowserSlotInteraction
{
	public const int TungstenSteelFluidCellCapacity = 512_000;

	public readonly struct Click
	{
		public bool Lmb  { get; init; }       // press-edge
		public bool Rmb  { get; init; }
		public bool Alt  { get; init; }
		public bool Ctrl { get; init; }
	}

	// Edge-tracked - caller passes previous frame's mouse-down state.
	public static Click Poll(bool prevLeftDown, bool prevRightDown)
	{
		var k = Main.keyState;
		return new Click
		{
			Lmb  = Main.mouseLeft  && !prevLeftDown,
			Rmb  = Main.mouseRight && !prevRightDown,
			Alt  = k.IsKeyDown(Keys.LeftAlt)     || k.IsKeyDown(Keys.RightAlt),
			Ctrl = k.IsKeyDown(Keys.LeftControl) || k.IsKeyDown(Keys.RightControl),
		};
	}

	// Instance overload preserves per-stack data (research blob etc.) so cheat
	// spawns AS DISPLAYED. Non-cheat paths key off display.type only.
	public static void HandleItem(Click c, Item display, bool inFavoritesPane,
		int? recipeAmount = null)
	{
		if (display is null || display.IsAir) return;
		bool cheat = (c.Lmb || c.Rmb) && c.Ctrl;
		if (cheat)
		{
			if (Main.GameModeInfo.IsJourneyMode)
			{
				int lmbCount = recipeAmount is > 0 ? recipeAmount.Value : 1;
				SpawnItemStack(display, c.Rmb ? MaxStackFor(display.type) : lmbCount);
			}
			return;
		}
		HandleItem(c, display.type, inFavoritesPane, recipeAmount);
	}

	public static void HandleItem(Click c, int itemType, bool inFavoritesPane,
		int? recipeAmount = null)
	{
		if (itemType <= 0) return;

		if (c.Lmb)
		{
			if (c.Alt)
			{
				FavoriteItem(itemType, inFavoritesPane);
			}
			else if (c.Ctrl)
			{
				if (Main.GameModeInfo.IsJourneyMode)
					SpawnItem(itemType, recipeAmount is > 0 ? recipeAmount.Value : 1);
			}
			else
			{
				HoverItemTracker.PushItem(itemType);
				GlobalRecipeBrowserSystem.OpenFiltered(itemType,
					GlobalRecipeBrowserState.BrowseFilter.Output);
			}
		}
		else if (c.Rmb)
		{
			if (c.Ctrl)
			{
				if (Main.GameModeInfo.IsJourneyMode) SpawnItem(itemType, MaxStackFor(itemType));
			}
			else
			{
				GlobalRecipeBrowserSystem.OpenFiltered(itemType,
					GlobalRecipeBrowserState.BrowseFilter.Input);
			}
		}
	}

	// recipeAmountMb is the per-recipe content amount (e.g. 144 mB) when the
	// slot is inside a recipe row; null elsewhere -> Ctrl+LMB gives a full cell.
	public static void HandleFluid(Click c, FluidType? fluid, int? recipeAmountMb,
		bool inFavoritesPane)
	{
		if (fluid is null) return;
		string id    = fluid.Id;
		string label = fluid.DisplayName;

		if (c.Lmb)
		{
			if (c.Alt)
			{
				FavoriteFluid(id, label, inFavoritesPane);
			}
			else if (c.Ctrl)
			{
				if (Main.GameModeInfo.IsJourneyMode)
					SpawnFluidCell(fluid, recipeAmountMb ?? TungstenSteelFluidCellCapacity);
			}
			else
			{
				HoverItemTracker.PushFluid(id);
				GlobalRecipeBrowserSystem.OpenFilteredFluid(id, label,
					GlobalRecipeBrowserState.BrowseFilter.Output);
			}
		}
		else if (c.Rmb)
		{
			if (c.Ctrl)
			{
				if (Main.GameModeInfo.IsJourneyMode)
					SpawnFluidCell(fluid, TungstenSteelFluidCellCapacity);
			}
			else
			{
				GlobalRecipeBrowserSystem.OpenFilteredFluid(id, label,
					GlobalRecipeBrowserState.BrowseFilter.Input);
			}
		}
	}

	// Loot-mode NPC source icon. Only Ctrl+click acts (NPCs aren't favoritable
	// and have no recipe-pivot); both Ctrl+LMB and Ctrl+RMB spawn one.
	public static void HandleNpc(Click c, int npcType)
	{
		if (npcType <= 0) return;
		if (!c.Ctrl || !Main.GameModeInfo.IsJourneyMode) return;
		if (c.Lmb || c.Rmb) SpawnNpc(npcType);
	}

	public static void HandleTag(Click c, string tagLabel, HashSet<int> members,
		int? recipeAmount = null)
	{
		if (members.Count == 0) return;
		if (c.Alt) return;   // "favorite which member?" ambiguous
		// Ctrl+click cheats the deterministic first member (lowest itemType).
		if (c.Ctrl)
		{
			if ((c.Lmb || c.Rmb) && Main.GameModeInfo.IsJourneyMode)
			{
				int first = FirstMember(members);
				if (first > 0)
				{
					int lmbCount = recipeAmount is > 0 ? recipeAmount.Value : 1;
					SpawnItem(first, c.Rmb ? MaxStackFor(first) : lmbCount);
				}
			}
			return;
		}
		if (c.Lmb)
			GlobalRecipeBrowserSystem.OpenFilteredTag(tagLabel, members,
				GlobalRecipeBrowserState.BrowseFilter.Output);
		else if (c.Rmb)
			GlobalRecipeBrowserSystem.OpenFilteredTag(tagLabel, members,
				GlobalRecipeBrowserState.BrowseFilter.Input);
	}

	private static int FirstMember(HashSet<int> members)
	{
		int best = 0;
		foreach (int t in members)
			if (t > 0 && (best == 0 || t < best)) best = t;
		return best;
	}

	private static void FavoriteItem(int itemType, bool inFavoritesPane)
	{
		if (inFavoritesPane) FavoritesRegistry.RemoveItem(itemType);
		else                 FavoritesRegistry.BringItemToFront(itemType);
	}

	private static void FavoriteFluid(string id, string label, bool inFavoritesPane)
	{
		if (inFavoritesPane) FavoritesRegistry.RemoveFluid(id);
		else                 FavoritesRegistry.BringFluidToFront(id, label);
	}

	private static int MaxStackFor(int itemType)
	{
		if (ContentSamples.ItemsByType.TryGetValue(itemType, out var sample) && sample.maxStack > 0)
			return sample.maxStack;
		return 999;
	}

	private static void SpawnItem(int itemType, int stack)
	{
		if (stack <= 0) return;
		var src = new EntitySource_DebugCommand("GTBrowserSlotInteraction");
		// PlayerGive: instant local insert, not a world-drop with grab-tick latency.
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(Main.LocalPlayer, src, itemType, stack);
	}

	// Insert-by-reference - QuickSpawnItem / Item.Clone(Item) drops just-written
	// per-stack data on some paths; GetItem hands the exact instance to a slot.
	private static void SpawnItemStack(Item template, int stack)
	{
		if (stack <= 0 || template is null || template.IsAir) return;
		template.stack = stack;
		var src = new EntitySource_DebugCommand("GTBrowserSlotInteraction");
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(Main.LocalPlayer, src, template);
	}

	// Tungsten-steel cell pre-filled with up to `amountMb`. Insert-by-reference
	// via GetItem since Item.Clone drops the just-Filled _fluidTag.
	private static void SpawnFluidCell(FluidType fluid, int amountMb)
	{
		if (amountMb <= 0) return;
		int cellItemType = ModContent.ItemType<TungstenSteelFluidCell>();
		int capped = amountMb > TungstenSteelFluidCellCapacity ? TungstenSteelFluidCellCapacity : amountMb;

		var item = new Item();
		item.SetDefaults(cellItemType);
		item.stack = 1;
		if (item.ModItem is FluidCellItem cell)
		{
			((IFluidHandlerItem)cell)
				.Fill(new FluidStack(fluid, capped), simulate: false);
		}
		var src = new EntitySource_DebugCommand("GTBrowserSlotInteraction");
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(Main.LocalPlayer, src, item);
	}

	private static void SpawnNpc(int npcType)
	{
		var p = Main.LocalPlayer;
		var src = new EntitySource_DebugCommand("GTBrowserSlotInteraction");
		// Spawn just above the player so the NPC drops into view (bestiary parity).
		int worldX = (int)p.Center.X;
		int worldY = (int)p.Center.Y - 32;
		NPC.NewNPC(src, worldX, worldY, npcType);
	}
}
