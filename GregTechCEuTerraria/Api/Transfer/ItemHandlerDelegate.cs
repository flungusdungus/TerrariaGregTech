#nullable enable
using GregTechCEuTerraria.Api.Capability;
using Terraria;

namespace GregTechCEuTerraria.Api.Transfer;

// Port of com.gregtechceu.gtceu.api.transfer.item.ItemHandlerDelegate.
//
// An IItemHandler that forwards every call to a wrapped `Inner` handler.
// Cover capability wrappers (conveyor / filter covers) subclass this and
// override Insert / Extract to gate transfer. The public `Inner` field lets a
// cover detect when the underlying machine handler instance changed and
// re-wrap (upstream compares `wrapper.delegate != defaultValue`).
public class ItemHandlerDelegate : IItemHandler
{
	public readonly IItemHandler Inner;

	public ItemHandlerDelegate(IItemHandler inner) => Inner = inner;

	public virtual int SlotCount => Inner.SlotCount;
	public virtual Item GetSlot(int slot) => Inner.GetSlot(slot);
	public virtual Item Insert(int slot, Item item, bool simulate) => Inner.Insert(slot, item, simulate);
	public virtual Item Extract(int slot, int maxAmount, bool simulate) => Inner.Extract(slot, maxAmount, simulate);
	public virtual int GetSlotLimit(int slot) => Inner.GetSlotLimit(slot);
	public virtual bool IsItemValid(int slot, Item item) => Inner.IsItemValid(slot, item);
}
