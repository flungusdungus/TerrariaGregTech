#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Transfer;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;

// Verbatim port of PipeTankList. Per-side IFluidHandler around a fluid
// pipe's per-channel tanks. FluidStack is an immutable struct here, so
// upstream's stack.copy() is a no-op and setAmount(n) -> WithAmount(n).
public sealed class PipeTankList : IFluidHandlerModifiable, IEnumerable<CustomFluidTank>
{
	private readonly IFluidPipeHost _pipe;
	private readonly CustomFluidTank[] _tanks;
	private readonly CoverSide _facing;

	public PipeTankList(IFluidPipeHost pipe, CoverSide facing, params CustomFluidTank[] fluidTanks)
	{
		_tanks  = fluidTanks;
		_pipe   = pipe;
		_facing = facing;
	}

	private int FindChannel(FluidStack stack)
	{
		if (stack.IsEmpty || _tanks is null)
			return -1;
		int empty = -1;
		for (int i = _tanks.Length - 1; i >= 0; i--)
		{
			FluidStack f = _tanks[i].Fluid;
			if (f.IsEmpty)
				empty = i;
			else if (f.SameTypeAs(stack))
				return i;
		}
		return empty;
	}

	public int TankCount => _tanks.Length;

	public FluidStack GetTank(int tank) => _tanks[tank].Fluid;

	public void SetFluidInTank(int tank, FluidStack stack) => _tanks[tank].SetFluid(stack);

	public int GetCapacity(int tank) => _tanks[tank].Capacity;

	public bool IsFluidValid(int tank, FluidStack stack) => _tanks[tank].IsFluidValid(stack);

	public int FullCapacity => _tanks.Length * _pipe.CapacityPerTank;

	public int Fill(FluidStack resource, bool simulate)
	{
		int channel;
		if (_pipe.IsBlocked(_facing) || resource.Amount < 0 || (channel = FindChannel(resource)) < 0) return 0;
		return Fill(resource, simulate, channel);
	}

	private int Fill(FluidStack resource, bool simulate, int channel)
	{
		if (channel >= _tanks.Length) return 0;
		CustomFluidTank tank = _tanks[channel];
		FluidStack currentFluid = tank.Fluid;

		if (currentFluid.IsEmpty || currentFluid.Amount <= 0)
		{
			int accept = Math.Min(_pipe.CapacityPerTank, resource.Amount);
			FluidStack newFluid = resource.WithAmount(accept);
			if (!simulate)
			{
				tank.SetFluid(newFluid);
				_pipe.ReceivedFrom(_facing);
				_pipe.CheckAndDestroy(newFluid);
			}
			return newFluid.Amount;
		}
		if (currentFluid.SameTypeAs(resource))
		{
			int toAdd = Math.Min(tank.Capacity - currentFluid.Amount, resource.Amount);
			if (toAdd > 0)
			{
				if (!simulate)
				{
					FluidStack updated = currentFluid.WithAmount(currentFluid.Amount + toAdd);
					tank.SetFluid(updated);
					_pipe.ReceivedFrom(_facing);
					_pipe.CheckAndDestroy(updated);
				}
				return toAdd;
			}
		}

		return 0;
	}

	public FluidStack Drain(int maxDrain, bool simulate)
	{
		if (maxDrain <= 0) return FluidStack.Empty;
		foreach (CustomFluidTank tank in _tanks)
		{
			FluidStack drained = tank.Drain(maxDrain, simulate);
			if (!drained.IsEmpty) return drained;
		}
		return FluidStack.Empty;
	}

	public FluidStack Drain(FluidStack resource, bool simulate)
	{
		if (resource.Amount <= 0) return FluidStack.Empty;
		foreach (CustomFluidTank tank in _tanks)
		{
			FluidStack drained = tank.Drain(resource, simulate);
			if (!drained.IsEmpty) return drained;
		}
		return FluidStack.Empty;
	}

	public IEnumerator<CustomFluidTank> GetEnumerator() => ((IEnumerable<CustomFluidTank>)_tanks).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => _tanks.GetEnumerator();
}
