#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.api.cover.filter.SimpleFluidFilter.
//
// 9-slot phantom-fluid filter, mirror of SimpleItemFilter for fluids.
//
// Documented adaptations:
//   - openConfigurator (the 3x3 phantom-tank WidgetGroup + its CustomFluidTank
//     scratch slots) is dropped - cover settings UI is a later phase. The
//     matching / amount model is ported verbatim.
//   - ignoreNbt matches on fluid Type only; non-ignoreNbt uses FluidStack
//     .SameTypeAs (type + NBT), verbatim with upstream's isFluidEqual split.
public sealed class SimpleFluidFilter : IFluidFilter
{
	public bool IsBlackList { get; private set; }
	public bool IgnoreNbt { get; private set; }
	public FluidStack[] Matches { get; } = new FluidStack[9];
	public int MaxStackSize { get; private set; } = 1;

	public Action OnUpdated { get; set; } = () => { };

	public SimpleFluidFilter()
	{
		for (int i = 0; i < Matches.Length; i++) Matches[i] = FluidStack.Empty;
	}

	public static SimpleFluidFilter LoadFilter(TagCompound tag)
	{
		var handler = new SimpleFluidFilter
		{
			IsBlackList = tag.GetBool("isBlackList"),
			IgnoreNbt = tag.GetBool("matchNbt"),
			// Verbatim with SimpleItemFilter - persist MaxStackSize so the
			// FilterHandler.Load -> OnFilterLoaded -> ConfigureFilter chain
			// doesn't clamp freshly-loaded amounts down to the default
			// Any-mode cap before the owning cover's Load restores transferMode.
			MaxStackSize = tag.ContainsKey("maxStackSize") ? tag.GetInt("maxStackSize") : 1,
		};
		if (tag.ContainsKey("matches"))
		{
			var list = tag.GetList<TagCompound>("matches");
			for (int i = 0; i < list.Count && i < handler.Matches.Length; i++)
				handler.Matches[i] = LoadStack(list[i]);
		}
		return handler;
	}

	// Mutates THIS filter in place from `tag` - the per-instance counterpart
	// of the static LoadFilter factory. Used by cross-mode filter sync on
	// fluid pipes (PipeCoverable.CopyFilterStateOnModeChange) where the
	// fluid covers expose UiFluidFilter directly and the dst SimpleFluidFilter
	// instance has to be replaced field-by-field (the cover doesn't expose
	// a setter to swap in a new SimpleFluidFilter).
	public void LoadFrom(TagCompound tag)
	{
		SetBlackList(tag.GetBool("isBlackList"));
		SetIgnoreNbt(tag.GetBool("matchNbt"));
		MaxStackSize = tag.ContainsKey("maxStackSize") ? tag.GetInt("maxStackSize") : 1;
		for (int i = 0; i < Matches.Length; i++) Matches[i] = FluidStack.Empty;
		if (tag.ContainsKey("matches"))
		{
			var list = tag.GetList<TagCompound>("matches");
			for (int i = 0; i < list.Count && i < Matches.Length; i++)
				Matches[i] = LoadStack(list[i]);
		}
		OnUpdated();
	}

	// Reset this filter to the "blank" state (empty whitelist, no NBT
	// matching). Used when CopyFilterStateOnModeChange copies from a blank
	// src - without this, dst would keep its prior matches.
	public void Reset()
	{
		SetBlackList(false);
		SetIgnoreNbt(false);
		for (int i = 0; i < Matches.Length; i++) Matches[i] = FluidStack.Empty;
		OnUpdated();
	}

	private static FluidStack LoadStack(TagCompound tag)
	{
		if (!tag.ContainsKey("fluid")) return FluidStack.Empty;
		return FluidRegistry.TryGet(tag.GetString("fluid"), out var ft)
			? new FluidStack(ft, tag.GetInt("amount"))
			: FluidStack.Empty;
	}

	private static TagCompound SaveStack(FluidStack stack)
	{
		var tag = new TagCompound();
		if (!stack.IsEmpty)
		{
			tag["fluid"] = stack.Type!.Id;
			tag["amount"] = stack.Amount;
		}
		return tag;
	}

	public bool IsBlank
	{
		get
		{
			if (IsBlackList || IgnoreNbt) return false;
			foreach (var m in Matches)
				if (!m.IsEmpty) return false;
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
		foreach (var m in Matches) list.Add(SaveStack(m));
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

	public bool Test(FluidStack other) => TestFluidAmount(other) > 0;

	public int TestFluidAmount(FluidStack fluidStack)
	{
		int totalFluidAmount = GetTotalConfiguredFluidAmount(fluidStack);
		if (IsBlackList)
			return totalFluidAmount > 0 ? 0 : int.MaxValue;
		return totalFluidAmount;
	}

	public int GetTotalConfiguredFluidAmount(FluidStack fluidStack)
	{
		int totalAmount = 0;
		foreach (var candidate in Matches)
		{
			if (candidate.IsEmpty) continue;
			bool match = IgnoreNbt
				? candidate.Type!.Id == fluidStack.Type?.Id
				: candidate.SameTypeAs(fluidStack);
			if (match) totalAmount += candidate.Amount;
		}
		return totalAmount;
	}

	public void SetMaxStackSize(int maxStackSize)
	{
		MaxStackSize = maxStackSize;
		for (int i = 0; i < Matches.Length; i++)
			if (!Matches[i].IsEmpty)
				Matches[i] = Matches[i].WithAmount(Math.Min(Matches[i].Amount, maxStackSize));
	}
}
