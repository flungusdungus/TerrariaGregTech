#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Api.Fluids;
using System;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Capabilities.Handlers;

// Single-tank fluid container. Optional Filter limits the tank to one fluid
// type (e.g. a dedicated steam tank). When no Filter, any fluid that fits the
// current content is accepted.
public sealed class SimpleFluidHandler : IFluidHandler
{
	private FluidStack _stored;
	public int Capacity { get; }
	public FluidType? Filter { get; }

	public SimpleFluidHandler(int capacity, FluidType? filter = null)
	{
		Capacity = capacity;
		Filter = filter;
	}

	public int TankCount => 1;
	public FluidStack GetTank(int tank) => _stored;
	public int GetCapacity(int tank) => Capacity;

	public bool IsFluidValid(int tank, FluidStack fluid) =>
		fluid.IsEmpty || Filter is null || fluid.Type!.Id == Filter.Id;

	public int Fill(FluidStack fluid, bool simulate)
	{
		if (fluid.IsEmpty) return 0;
		if (!IsFluidValid(0, fluid)) return 0;
		if (!_stored.IsEmpty && !_stored.SameTypeAs(fluid)) return 0;

		int headroom = Capacity - _stored.Amount;
		int accepted = Math.Min(fluid.Amount, headroom);
		if (accepted <= 0) return 0;

		if (!simulate)
			_stored = new FluidStack(fluid.Type!, _stored.Amount + accepted);
		return accepted;
	}

	public FluidStack Drain(int maxAmount, bool simulate)
	{
		if (_stored.IsEmpty || maxAmount <= 0) return FluidStack.Empty;
		int drained = Math.Min(maxAmount, _stored.Amount);
		var result = new FluidStack(_stored.Type!, drained);
		if (!simulate)
		{
			int remaining = _stored.Amount - drained;
			_stored = remaining > 0 ? new FluidStack(_stored.Type!, remaining) : FluidStack.Empty;
		}
		return result;
	}

	public FluidStack Drain(FluidStack fluidStack, bool simulate)
	{
		if (fluidStack.IsEmpty) return FluidStack.Empty;
		if (_stored.IsEmpty || !_stored.SameTypeAs(fluidStack)) return FluidStack.Empty;
		return Drain(fluidStack.Amount, simulate);
	}

	// --- Persistence helpers (used by item / tile-entity Save/Load) ---

	public TagCompound Save()
	{
		var tag = new TagCompound();
		if (!_stored.IsEmpty)
		{
			tag["id"] = _stored.Type!.Id;
			tag["amount"] = _stored.Amount;
		}
		return tag;
	}

	public void Load(TagCompound tag)
	{
		if (!tag.ContainsKey("id")) { _stored = FluidStack.Empty; return; }
		string id = tag.GetString("id");
		int amount = tag.GetInt("amount");
		_stored = FluidRegistry.TryGet(id, out var type)
			? new FluidStack(type, amount)
			: FluidStack.Empty;
	}

	// Deep-copy for item Clone scenarios.
	public SimpleFluidHandler Copy()
	{
		var clone = new SimpleFluidHandler(Capacity, Filter);
		if (!_stored.IsEmpty) clone._stored = _stored;
		return clone;
	}
}
