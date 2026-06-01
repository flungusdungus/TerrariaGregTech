#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Util;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// Port of common.cover.detector.AdvancedFluidDetectorCover. Fluid twin of
// AdvancedItemDetectorCover.
public class AdvancedFluidDetectorCover : FluidDetectorCover, IUICover, IAdvancedDetectorCover
{
	private const int DefaultMin = 64;
	private const int DefaultMax = 512;

	private int _minValue = DefaultMin;
	private int _maxValue = DefaultMax;
	private bool _isLatched;
	protected readonly FluidFilterHandler FilterHandler;

	public override FluidFilterHandler? UiFluidFilterHandler => FilterHandler;

	public AdvancedFluidDetectorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide)
	{
		FilterHandler = FilterHandlers.Fluid(this);
	}

	public long MinValue => _minValue;
	public long MaxValue => _maxValue;
	public bool IsLatched => _isLatched;

	public void SetMinValue(int minValue) => _minValue = Math.Clamp(minValue, 0, _maxValue - 1);
	public void SetMaxValue(int maxValue) => _maxValue = Math.Max(maxValue, 0);
	public void SetLatched(bool latched) => _isLatched = latched;

	// field 2=min, 3=max, 4=latch. 1 (invert) falls through.
	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 2: SetMinValue((int)Math.Clamp(value, 0, int.MaxValue)); break;
			case 3: SetMaxValue((int)Math.Clamp(value, 0, int.MaxValue)); break;
			case 4: SetLatched(value != 0); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	public override List<Item> GetAdditionalDrops()
	{
		var list = base.GetAdditionalDrops();
		if (!FilterHandler.FilterItem.IsAir) list.Add(FilterHandler.FilterItem);
		return list;
	}

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;

		var filter = FilterHandler.GetFilter();
		var handler = GetFluidHandler();
		if (handler == null) return;

		long storedFluid = 0;
		for (int tank = 0; tank < handler.TankCount; tank++)
		{
			var content = handler.GetTank(tank);
			if (!content.IsEmpty && filter.Test(content)) storedFluid += content.Amount;
		}

		SetRedstoneSignalOutput(_isLatched
			? RedstoneUtil.ComputeLatchedRedstoneBetweenValues((float)storedFluid, _maxValue, _minValue, IsInverted,
				RedstoneSignalOutput)
			: RedstoneUtil.ComputeRedstoneBetweenValues(storedFluid, _maxValue, _minValue, IsInverted));
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["min"] = _minValue;
		tag["max"] = _maxValue;
		tag["latched"] = _isLatched;
		var filterTag = new TagCompound();
		FilterHandler.Save(filterTag);
		tag["filter"] = filterTag;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("min")) _minValue = tag.GetInt("min");
		if (tag.ContainsKey("max")) _maxValue = tag.GetInt("max");
		_isLatched = tag.GetBool("latched");
		if (tag.ContainsKey("filter")) FilterHandler.Load(tag.GetCompound("filter"));
	}
}
