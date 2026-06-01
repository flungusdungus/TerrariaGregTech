#nullable enable
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.api.cover.filter.SimpleItemFilter.
//
// A 9-slot phantom-item filter - an item passes if it matches any configured
// slot; the configured stack count is the per-item amount.
//
// Documented adaptations:
//   - openConfigurator (the 3x3 phantom-slot WidgetGroup) is dropped - cover
//     settings UI is a later phase. The 9-slot `matches` model and all matching
//     / amount logic are ported verbatim.
//   - GTUtil.isSameItemSameTags / ItemStack.isSameItem both collapse to a type
//     compare. A Terraria Item carries no per-instance NBT that filter matching
//     would distinguish, so `ignoreNbt` is preserved as a persisted flag (for
//     save-compat and a future UI toggle) but matching is type-only either way.
public sealed class SimpleItemFilter : IItemFilter
{
	public bool IsBlackList { get; private set; }
	public bool IgnoreNbt { get; private set; }
	public Item[] Matches { get; } = new Item[9];
	public int MaxStackSize { get; private set; } = 1;

	public Action OnUpdated { get; set; } = () => { };

	public SimpleItemFilter()
	{
		for (int i = 0; i < Matches.Length; i++) Matches[i] = new Item();
	}

	public static SimpleItemFilter LoadFilter(TagCompound tag)
	{
		var handler = new SimpleItemFilter
		{
			IsBlackList = tag.GetBool("isBlackList"),
			IgnoreNbt = tag.GetBool("matchNbt"),
			// Verbatim upstream `@Persisted @DropSaved public int maxStackSize = 1`.
			// Without this round-trip, FilterHandler.Load fires OnFilterLoaded ->
			// ConfigureFilter on the owning cover BEFORE the cover's own Load
			// has restored _transferMode, so the freshly-loaded matches get
			// clamped down to the default-Any cap (1) and never recover.
			MaxStackSize = tag.ContainsKey("maxStackSize") ? tag.GetInt("maxStackSize") : 1,
		};
		if (tag.ContainsKey("matches"))
		{
			var list = tag.GetList<TagCompound>("matches");
			for (int i = 0; i < list.Count && i < handler.Matches.Length; i++)
				handler.Matches[i] = ItemIO.Load(list[i]);
		}
		return handler;
	}

	// In-place counterpart of LoadFilter - mutates THIS instance from `tag`.
	// Used by pipe cross-mode filter copy (PipeCoverable.CopyFilterStateOnModeChange)
	// which needs to populate a destination cover's UiItemFilter without being
	// able to swap out the instance.
	public void LoadFrom(TagCompound tag)
	{
		SetBlackList(tag.GetBool("isBlackList"));
		SetIgnoreNbt(tag.GetBool("matchNbt"));
		// Restore MaxStackSize BEFORE loading the matches so the per-match
		// clamp in SetMaxStackSize doesn't fight a freshly-loaded stack.
		MaxStackSize = tag.ContainsKey("maxStackSize") ? tag.GetInt("maxStackSize") : 1;
		for (int i = 0; i < Matches.Length; i++) Matches[i] = new Item();
		if (tag.ContainsKey("matches"))
		{
			var list = tag.GetList<TagCompound>("matches");
			for (int i = 0; i < list.Count && i < Matches.Length; i++)
				Matches[i] = ItemIO.Load(list[i]);
		}
		OnUpdated();
	}

	// Reset to the blank state - empty whitelist, no NBT matching. Used when
	// CopyFilterStateOnModeChange copies from a blank src.
	public void Reset()
	{
		SetBlackList(false);
		SetIgnoreNbt(false);
		for (int i = 0; i < Matches.Length; i++) Matches[i] = new Item();
		OnUpdated();
	}

	public bool IsBlank
	{
		get
		{
			if (IsBlackList || IgnoreNbt) return false;
			foreach (var m in Matches)
				if (!m.IsAir) return false;
			return true;
		}
	}

	public TagCompound? SaveFilter()
	{
		if (IsBlank) return null;
		var tag = new TagCompound
		{
			["isBlackList"] = IsBlackList,
			["matchNbt"] = IgnoreNbt,
			["maxStackSize"] = MaxStackSize,
		};
		var list = new List<TagCompound>(Matches.Length);
		foreach (var m in Matches) list.Add(ItemIO.Save(m));
		tag["matches"] = list;
		return tag;
	}

	public void SetBlackList(bool blackList)
	{
		IsBlackList = blackList;
		OnUpdated();
	}

	public void SetIgnoreNbt(bool ignoreNbt)
	{
		IgnoreNbt = ignoreNbt;
		OnUpdated();
	}

	public bool Test(Item itemStack) => TestItemCount(itemStack) > 0;

	public int TestItemCount(Item itemStack)
	{
		int totalItemCount = GetTotalConfiguredItemCount(itemStack);
		if (IsBlackList)
			return totalItemCount > 0 ? 0 : int.MaxValue;
		return totalItemCount;
	}

	public int GetTotalConfiguredItemCount(Item itemStack)
	{
		int totalCount = 0;
		foreach (var candidate in Matches)
			if (!candidate.IsAir && candidate.type == itemStack.type)
				totalCount += candidate.stack;
		return totalCount;
	}

	public void SetMaxStackSize(int maxStackSize)
	{
		MaxStackSize = maxStackSize;
		foreach (var match in Matches)
			if (!match.IsAir) match.stack = Math.Min(match.stack, maxStackSize);
	}
}
