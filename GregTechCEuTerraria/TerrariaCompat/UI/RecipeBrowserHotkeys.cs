#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Items.Fluids;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Hover hotkeys for the global recipe browser. R/U over an item or fluid
// (inventory, machine slot, recipe row) open the browser scoped to
// "how to obtain" / "used as ingredient". Rebindable.
//
// tML defaults aren't auto-applied to input profiles - players see our binds
// as UNBOUND until "Reset to Default" or manual assignment. This matches
// ExampleMod / MagicStorage / HEROsMod / QuestBooks convention.
//
// When a press is handled it is fully CONSUMED for the duration of the hold:
// every other trigger bound to the same physical key is cleared in
// PostUpdateInput (after trigger poll, before CopyInto) so whatever vanilla
// or mod action shares that key does not fire.
public sealed class RecipeBrowserKeybinds : ModSystem
{
	public static ModKeybind? HowToObtain;
	public static ModKeybind? UsedAsIngredient;

	private static readonly InputMode[] KeyboardModes = { InputMode.Keyboard, InputMode.KeyboardUI };

	// True while we own the in-progress press and must keep its key consumed.
	private bool _ownObtain;
	private bool _ownUsed;

	public override void Load()
	{
		// Display names registered first so Controls shows readable text.
		Language.GetOrRegister(
			"Mods.GregTechCEuTerraria.Keybinds.RecipeBrowserHowToObtain.DisplayName",
			() => "Recipe browser - how to obtain hovered item");
		Language.GetOrRegister(
			"Mods.GregTechCEuTerraria.Keybinds.RecipeBrowserUsedAsIngredient.DisplayName",
			() => "Recipe browser - recipes that use hovered item");

		HowToObtain      = KeybindLoader.RegisterKeybind(Mod, "RecipeBrowserHowToObtain", Keys.R);
		UsedAsIngredient = KeybindLoader.RegisterKeybind(Mod, "RecipeBrowserUsedAsIngredient", Keys.U);
	}

	public override void Unload()
	{
		HowToObtain = null;
		UsedAsIngredient = null;
	}

	public override void PostUpdateInput()
	{
		if (Main.dedServ) return;
		Handle(HowToObtain, GlobalRecipeBrowserState.BrowseFilter.Output, ref _ownObtain);
		Handle(UsedAsIngredient, GlobalRecipeBrowserState.BrowseFilter.Input, ref _ownUsed);

		// Esc closes the browser; ConsumeEscape keeps the underlying inventory open.
		if (GlobalRecipeBrowserSystem.IsOpen && ModalEscape.EscJustPressed)
		{
			GlobalRecipeBrowserSystem.Close();
			ModalEscape.ConsumeEscape();
		}
	}

	private void Handle(ModKeybind? kb, GlobalRecipeBrowserState.BrowseFilter dir, ref bool owned)
	{
		if (kb is null) return;

		// Read state BEFORE ConsumeKey() clears it.
		// kb.Current / kb.JustPressed throw KeyNotFoundException when the
		// player has no key assigned in their input profile - the documented
		// UNBOUND-until-"Reset to Default" state (see file header). Catch
		// and treat as unbound = no input.
		bool held, justPressed;
		try { held = kb.Current; justPressed = kb.JustPressed; }
		catch (KeyNotFoundException) { return; }

		if (!held) owned = false;

		if (justPressed && TryOpenBrowser(dir))
			owned = true;

		// Swallow other actions on the same key for the whole hold (not just
		// the JustPressed frame - a 2-frame hold would otherwise leak vanilla).
		if (owned && held)
			ConsumeKey(kb);
	}

	// Returns false (= not claimed -> not consumed) over empty space, so the
	// key keeps its normal vanilla behaviour.
	private static bool TryOpenBrowser(GlobalRecipeBrowserState.BrowseFilter dir)
	{
		// BrowserHover wins - covers fluids + composited cells that
		// Main.HoverItem doesn't. Fall back to HoverItem otherwise.
		if (BrowserHover.Fresh)
		{
			if (BrowserHover.TagItems is not null && BrowserHover.TagLabel is not null)
			{
				GlobalRecipeBrowserSystem.OpenFilteredTag(
					BrowserHover.TagLabel, BrowserHover.TagItems, dir);
				return true;
			}
			if (BrowserHover.ItemType > 0)
			{
				GlobalRecipeBrowserSystem.OpenFiltered(BrowserHover.ItemType, dir);
				return true;
			}
			if (BrowserHover.FluidId is not null)
			{
				GlobalRecipeBrowserSystem.OpenFilteredFluid(
					BrowserHover.FluidId, BrowserHover.FluidLabel ?? BrowserHover.FluidId, dir);
				return true;
			}
			return false;
		}

		Item h = Main.HoverItem;
		if (h is null || h.IsAir) return false;
		// Filled cell/bucket carries a FluidType - route to the fluid-scoped
		// browser since recipes consume them as a FluidIngredient.
		if (TryResolveHoveredFluid(h, out string? fluidId, out string? label))
		{
			GlobalRecipeBrowserSystem.OpenFilteredFluid(fluidId!, label!, dir);
			return true;
		}
		GlobalRecipeBrowserSystem.OpenFiltered(h.type, dir);
		return true;
	}

	private static bool TryResolveHoveredFluid(Item item, out string? fluidId, out string? label)
	{
		// 3-tier resolver: vanilla bucket -> GT FluidBucketItem -> FluidCellItem.
		var vanilla = VanillaFluidBridge.StackFor(item.type);
		if (!vanilla.IsEmpty)
		{
			fluidId = vanilla.Type!.Id;
			label   = vanilla.Type.DisplayName;
			return true;
		}
		if (item.ModItem is FluidBucketItem bucket && bucket.Fluid is { } fluid)
		{
			fluidId = fluid.Id;
			label   = fluid.DisplayName;
			return true;
		}
		if (item.ModItem is FluidCellItem cell)
		{
			var stack = cell.GetFluidStack();
			if (!stack.IsEmpty)
			{
				fluidId = stack.Type!.Id;
				label   = stack.Type.DisplayName;
				return true;
			}
		}
		fluidId = null;
		label   = null;
		return false;
	}

	// Clears every trigger sharing a physical key with `kb` so CopyInto carries
	// cleared triggers into the control fields - no vanilla action fires.
	private static void ConsumeKey(ModKeybind kb)
	{
		foreach (var mode in KeyboardModes)
		{
			List<string> keys;
			try { keys = kb.GetAssignedKeys(mode); }
			catch { continue; }
			ConsumePhysicalKeys(keys, mode);
		}
	}

	// Shared via ModalEscape for every modal's Esc handler; alias for R/U.
	private static void ConsumePhysicalKeys(ICollection<string> physicalKeys, InputMode mode)
		=> ModalEscape.ConsumePhysicalKeys(physicalKeys, mode);
}
