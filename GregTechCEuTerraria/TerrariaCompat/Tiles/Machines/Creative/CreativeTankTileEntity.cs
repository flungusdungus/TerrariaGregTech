#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Creative;

// Port of com.gregtechceu.gtceu.common.machine.storage.CreativeTankMachine.
// Infinite fluid source for debug: configurable source fluid (_stored), mB per
// AutoOutput cycle (MBPerCycle), cycle period (TicksPerCycle). Fill matching
// source -> accepted, mismatch -> 0; Drain always returns MBPerCycle of the
// source (never depletes); AutoOutput fires on TicksPerCycle.
//
// DEVIATION: InfiniteCache collapsed onto the machine (override
// Fill/Drain/GetTank/GetCapacity), same shape as SuperTank.
public sealed class CreativeTankTileEntity : SuperTankTileEntity
{
	public CreativeTankTileEntity() { }

	protected override string Label => Definition?.Label ?? "Creative Tank";

	private int _mBPerCycle    = 1000;
	private int _ticksPerCycle = 1;

	public int MBPerCycle
	{
		get => _mBPerCycle;
		set => _mBPerCycle = Math.Max(1, value);
	}

	public int TicksPerCycle
	{
		get => _ticksPerCycle;
		set
		{
			_ticksPerCycle = Math.Max(1, value);
			if (AutoOutput is not null) AutoOutput.TicksPerCycle = _ticksPerCycle;
		}
	}

	// upstream updateStored(fluid): copy the fluid type into _stored (count-1
	// marker). Empty clears the source.
	public void SetSourceFluid(FluidType? type)
	{
		if (type is null)
		{
			_stored = FluidStack.Empty;
			_storedAmount = 0;
		}
		else
		{
			_stored = new FluidStack(type, 1);
			_storedAmount = 1;
		}
	}

	// upstream InfiniteCache overrides.
	public override FluidStack GetTank(int tank) =>
		_stored.IsEmpty ? FluidStack.Empty : _stored.WithAmount(_mBPerCycle);

	public override int GetCapacity(int tank) => 1000;   // upstream verbatim

	public override bool IsFluidValid(int tank, FluidStack stack) => true;

	public override int Fill(FluidStack resource, bool simulate)
	{
		// Matching source -> accepted (resource.Amount), else 0.
		if (resource.IsEmpty) return 0;
		if (!_stored.IsEmpty && _stored.SameTypeAs(resource)) return resource.Amount;
		return 0;
	}

	public override FluidStack Drain(int maxAmount, bool simulate)
	{
		// Always MBPerCycle of the source (ignores maxAmount, per upstream).
		if (_stored.IsEmpty) return FluidStack.Empty;
		return _stored.WithAmount(_mBPerCycle);
	}

	public override FluidStack Drain(FluidStack fluid, bool simulate)
	{
		if (fluid.IsEmpty || !_stored.SameTypeAs(fluid)) return FluidStack.Empty;
		return fluid.WithAmount(_mBPerCycle);
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["mBPerCycle"]    = _mBPerCycle;
		tag["ticksPerCycle"] = _ticksPerCycle;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		_mBPerCycle    = tag.ContainsKey("mBPerCycle")    ? Math.Max(1, tag.GetInt("mBPerCycle"))    : 1000;
		_ticksPerCycle = tag.ContainsKey("ticksPerCycle") ? Math.Max(1, tag.GetInt("ticksPerCycle")) : 1;
		if (AutoOutput is not null) AutoOutput.TicksPerCycle = _ticksPerCycle;
	}

	public override void WritePortableData(TagCompound tag)
	{
		if (!_stored.IsEmpty) tag["fluidId"] = _stored.Type!.Id;
		tag["mBPerCycle"]    = _mBPerCycle;
		tag["ticksPerCycle"] = _ticksPerCycle;
	}

	public override void ReadPortableData(TagCompound tag)
	{
		if (tag.ContainsKey("fluidId") && FluidRegistry.TryGet(tag.GetString("fluidId"), out var t))
		{
			_stored = new FluidStack(t, 1);
			_storedAmount = 1;
		}
		if (tag.ContainsKey("mBPerCycle"))    _mBPerCycle    = Math.Max(1, tag.GetInt("mBPerCycle"));
		if (tag.ContainsKey("ticksPerCycle")) _ticksPerCycle = Math.Max(1, tag.GetInt("ticksPerCycle"));
		if (AutoOutput is not null) AutoOutput.TicksPerCycle = _ticksPerCycle;
	}

	public override void AppendTooltip(List<string> lines)
	{
		// Skip SuperTank's stored/lock/void lines; just header + source info.
		lines.Add(DisplayName);
		lines.Add(_stored.IsEmpty ? "Source: (unset)" : $"Source: {_stored.Type!.DisplayName}");
		lines.Add($"Rate: {_mBPerCycle:N0} mB / {_ticksPerCycle}t");
		if (!IsAutoOutput) lines.Add("Auto-output: disabled");
	}
}
