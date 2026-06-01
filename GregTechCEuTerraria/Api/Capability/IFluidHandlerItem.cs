#nullable enable
using Terraria;

namespace GregTechCEuTerraria.Api.Capability;

// Item-level fluid handler. Mirrors upstream Forge's IFluidHandlerItem -
// an item stack that exposes IFluidHandler-style fill/drain against fluid
// stored in its NBT. Used by FluidCellItem so machine UIs can treat cells
// uniformly with tanks via the same fill/drain calls.
//
// The Container property exposes the backing ItemStack so callers can detect
// the case where Fill/Drain consumed the LAST mB and the cell transitioned
// from filled -> empty. Implementations typically swap the underlying item
// type at that boundary (empty_cell <-> universal_fluid_cell etc.) and the
// caller writes Container back to the slot.
public interface IFluidHandlerItem : IFluidHandler
{
	// The backing item - the slot the player is interacting with. Caller
	// reads this AFTER Fill/Drain to detect item-type transitions.
	Item Container { get; }
}
