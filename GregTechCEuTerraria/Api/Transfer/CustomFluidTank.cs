#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Transfer;

// LOCKED - port of
// com.gregtechceu.gtceu.api.transfer.fluid.CustomFluidTank.
//
// Single-fluid bounded tank with content-change callback + optional
// validator. Upstream extends Forge's `FluidTank`; we reproduce its
// surface inline.
//
// Documented adaptations:
//   - Forge FluidStack -> our FluidStack (record carrying Type + Amount + NBT).
//   - INBTSerializable<CompoundTag> -> SerializeNBT/DeserializeNBT(TagCompound).
//   - FluidAction (SIMULATE/EXECUTE) collapsed to a bool `simulate` parameter.
//   - Implements IFluidHandler as a single-tank handler - upstream's
//     CustomFluidTank extends FluidTank which implements IFluidHandler. This
//     makes a raw storage usable directly as the per-tank handler returned by
//     IFluidHandler.GetTankAccess. No IO direction gating (a raw tank has no
//     direction) - exactly what the UI-interaction path needs.
public class CustomFluidTank : IFluidHandlerModifiable
{
	// Single-tank handler - `SetFluidInTank` for IFluidHandlerModifiable
	// delegates to `SetFluid` (tank index ignored - there's only one).
	public void SetFluidInTank(int tank, FluidStack stack) => SetFluid(stack);


	public int Capacity { get; protected set; }
	public FluidStack Fluid { get; protected set; } = FluidStack.Empty;

	public Action OnContentsChangedAction { get; set; } = () => { };

	// Per-fluid validator. Default accepts any fluid.
	public Predicate<FluidStack> Validator { get; set; } = _ => true;

	public CustomFluidTank(int capacity) { Capacity = capacity; }

	public CustomFluidTank(int capacity, Predicate<FluidStack> validator)
	{
		Capacity = capacity;
		Validator = validator;
	}

	public CustomFluidTank(FluidStack stack) : this(stack.Amount)
	{
		SetFluid(stack);
	}

	// === Accessors ==========================================================

	public bool IsEmpty => Fluid.IsEmpty;
	public int FluidAmount => Fluid.IsEmpty ? 0 : Fluid.Amount;

	public virtual int GetCapacity() => Capacity;

	public virtual bool IsFluidValid(FluidStack stack) => Validator(stack);

	public virtual void SetFluid(FluidStack stack)
	{
		Fluid = stack;   // FluidStack is a struct; null isn't possible
		OnContentsChanged();
	}

	protected virtual void OnContentsChanged() => OnContentsChangedAction();

	// === Fill / drain (mirrors Forge FluidTank) =============================

	public virtual int Fill(FluidStack resource, bool simulate)
	{
		if (resource.IsEmpty || !IsFluidValid(resource)) return 0;
		if (Fluid.IsEmpty)
		{
			int accept = Math.Min(resource.Amount, Capacity);
			if (!simulate)
			{
				Fluid = new FluidStack(resource.Type!, accept, resource.Nbt);
				OnContentsChanged();
			}
			return accept;
		}
		if (!Fluid.SameTypeAs(resource)) return 0;
		int room = Capacity - Fluid.Amount;
		if (room <= 0) return 0;
		int take = Math.Min(resource.Amount, room);
		if (!simulate)
		{
			Fluid = new FluidStack(Fluid.Type!, Fluid.Amount + take, Fluid.Nbt);
			OnContentsChanged();
		}
		return take;
	}

	public virtual FluidStack Drain(int maxDrain, bool simulate)
	{
		if (maxDrain <= 0 || Fluid.IsEmpty) return FluidStack.Empty;
		int drained = Math.Min(Fluid.Amount, maxDrain);
		var out_ = new FluidStack(Fluid.Type!, drained, Fluid.Nbt);
		if (!simulate)
		{
			int rem = Fluid.Amount - drained;
			Fluid = rem > 0 ? new FluidStack(Fluid.Type!, rem, Fluid.Nbt) : FluidStack.Empty;
			OnContentsChanged();
		}
		return out_;
	}

	public virtual FluidStack Drain(FluidStack resource, bool simulate)
	{
		if (resource.IsEmpty || Fluid.IsEmpty || !Fluid.SameTypeAs(resource)) return FluidStack.Empty;
		return Drain(resource.Amount, simulate);
	}

	// === IFluidHandler (single-tank) ========================================
	// Fill / Drain above already match the IFluidHandler signatures; these
	// complete the surface. No IO direction - a raw tank IS the direction-free
	// storage. The Validator filter is still enforced through Fill.
	public int TankCount => 1;
	public FluidStack GetTank(int tank) => Fluid;
	public int GetCapacity(int tank) => Capacity;
	public bool IsFluidValid(int tank, FluidStack fluid) => IsFluidValid(fluid);

	// === Persistence ========================================================

	public TagCompound SerializeNBT()
	{
		var tag = new TagCompound();
		tag["capacity"] = Capacity;
		if (Fluid.IsEmpty)
		{
			tag["isNull"] = true;
		}
		else
		{
			tag["fluid"]  = Fluid.Type!.Id;
			tag["amount"] = Fluid.Amount;
			if (Fluid.Nbt is not null) tag["nbt"] = Fluid.Nbt;
		}
		return tag;
	}

	public void DeserializeNBT(TagCompound tag)
	{
		if (tag.ContainsKey("capacity")) Capacity = tag.GetInt("capacity");
		if (tag.GetBool("isNull") || !tag.ContainsKey("fluid"))
		{
			Fluid = FluidStack.Empty;
			return;
		}
		string fluidId = tag.GetString("fluid");
		int amount = tag.GetInt("amount");
		var nbt = tag.ContainsKey("nbt") ? tag.Get<TagCompound>("nbt") : null;
		if (FluidRegistry.TryGet(fluidId, out var ft))
			Fluid = new FluidStack(ft, amount, nbt);
		else
			Fluid = FluidStack.Empty;
	}
}
