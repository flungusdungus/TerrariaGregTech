#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Capabilities.Handlers;

// Extract-only views over an IItemHandler / IFluidHandler - our analogue of
// upstream's IOFilteredInvWrapper constructed with io == IO.OUT. A machine's
// GetItemHandlerCap / GetFluidHandlerCap hands one of these back when a
// neighbour queries the side whose input is gated off (the configured output
// side with allow-input-from-output-side disabled): reads and extraction pass
// straight through, insertion is refused.

public sealed class ExtractOnlyItemHandler : IItemHandler
{
	private readonly IItemHandler _inner;
	public ExtractOnlyItemHandler(IItemHandler inner) => _inner = inner;

	public int SlotCount => _inner.SlotCount;
	public Item GetSlot(int slot) => _inner.GetSlot(slot);
	public Item Extract(int slot, int maxAmount, bool simulate) => _inner.Extract(slot, maxAmount, simulate);
	public int GetSlotLimit(int slot) => _inner.GetSlotLimit(slot);

	// Insertion refused - the whole point of the wrapper. IsItemValid false so
	// callers short-circuit before Insert; Insert returns the stack untouched.
	public bool IsItemValid(int slot, Item item) => false;
	public Item Insert(int slot, Item item, bool simulate) => item;
}

public sealed class ExtractOnlyFluidHandler : IFluidHandler
{
	private readonly IFluidHandler _inner;
	public ExtractOnlyFluidHandler(IFluidHandler inner) => _inner = inner;

	public int TankCount => _inner.TankCount;
	public FluidStack GetTank(int tank) => _inner.GetTank(tank);
	public int GetCapacity(int tank) => _inner.GetCapacity(tank);
	public FluidStack Drain(int maxAmount, bool simulate) => _inner.Drain(maxAmount, simulate);
	public FluidStack Drain(FluidStack fluid, bool simulate) => _inner.Drain(fluid, simulate);

	// Insertion refused.
	public bool IsFluidValid(int tank, FluidStack fluid) => false;
	public int Fill(FluidStack fluid, bool simulate) => 0;
}

// Insert-only views - the mirror of the extract-only wrappers above. Used by the
// long-distance pipeline INPUT endpoint: it exposes a handler that forwards
// inserts to the far OUTPUT endpoint's inventory but never lets anything be
// pulled back out (verbatim upstream LDItem/LDFluidEndpointMachine wrapper -
// extractItem / drain return EMPTY).

public sealed class InsertOnlyItemHandler : IItemHandler
{
	private readonly IItemHandler _inner;
	public InsertOnlyItemHandler(IItemHandler inner) => _inner = inner;

	public int SlotCount => _inner.SlotCount;
	public Item GetSlot(int slot) => _inner.GetSlot(slot);
	public Item Insert(int slot, Item item, bool simulate) => _inner.Insert(slot, item, simulate);
	public int GetSlotLimit(int slot) => _inner.GetSlotLimit(slot);
	public bool IsItemValid(int slot, Item item) => _inner.IsItemValid(slot, item);

	// Extraction refused.
	public Item Extract(int slot, int maxAmount, bool simulate) => new();
}

public sealed class FillOnlyFluidHandler : IFluidHandler
{
	private readonly IFluidHandler _inner;
	public FillOnlyFluidHandler(IFluidHandler inner) => _inner = inner;

	public int TankCount => _inner.TankCount;
	public FluidStack GetTank(int tank) => _inner.GetTank(tank);
	public int GetCapacity(int tank) => _inner.GetCapacity(tank);
	public bool IsFluidValid(int tank, FluidStack fluid) => _inner.IsFluidValid(tank, fluid);
	public int Fill(FluidStack fluid, bool simulate) => _inner.Fill(fluid, simulate);

	// Drain refused.
	public FluidStack Drain(int maxAmount, bool simulate) => FluidStack.Empty;
	public FluidStack Drain(FluidStack fluid, bool simulate) => FluidStack.Empty;
}
