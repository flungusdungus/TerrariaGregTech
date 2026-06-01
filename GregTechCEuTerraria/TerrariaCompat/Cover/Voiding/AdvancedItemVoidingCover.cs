#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Voiding;

// Port of common.cover.voiding.AdvancedItemVoidingCover. Adds VOID_OVERFLOW
// mode - keep a per-type configured amount, void the surplus. UI + copyConfig
// dropped; defaults VOID_ANY / 1 behave like a basic ItemVoidingCover.
public class AdvancedItemVoidingCover : ItemVoidingCover, IAdvancedVoidingCover
{
	private VoidingMode _voidingMode = VoidingMode.VoidAny;
	protected int _globalVoidingLimit = 1;

	public AdvancedItemVoidingCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public VoidingMode VoidingMode => _voidingMode;
	public int GlobalVoidingLimit => _globalVoidingLimit;
	public int VoidLimit => _globalVoidingLimit;

	// field 1=voiding mode, 2=per-type overflow limit. 0 (working-enabled)
	// falls through.
	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 1: SetVoidingMode((VoidingMode)System.Math.Clamp(value, 0, 1)); break;
			case 2: _globalVoidingLimit = (int)System.Math.Clamp(value, 0, 1024); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	protected override void DoVoidItems()
	{
		var handler = GetOwnItemHandler();
		if (handler == null) return;

		switch (_voidingMode)
		{
			case VoidingMode.VoidAny:
				VoidAny(handler);
				break;
			case VoidingMode.VoidOverflow:
				VoidOverflow(handler);
				break;
		}
	}

	private void VoidOverflow(IItemHandler handler)
	{
		var sourceItemAmounts = CountInventoryItemsByType(handler);

		foreach (var itemInfo in sourceItemAmounts.Values)
		{
			int itemToVoidAmount = itemInfo.TotalCount - GetFilteredItemAmount(itemInfo.ItemStack);
			if (itemToVoidAmount <= 0) continue;

			for (int slot = 0; slot < handler.SlotCount; slot++)
			{
				Item current = handler.GetSlot(slot);
				if (!current.IsAir && current.type == itemInfo.ItemStack.type)
				{
					Item extracted = handler.Extract(slot, itemToVoidAmount, false);
					if (!extracted.IsAir) itemToVoidAmount -= extracted.stack;
				}
				if (itemToVoidAmount == 0) break;
			}
		}
	}

	private int GetFilteredItemAmount(Item itemStack)
	{
		if (!FilterHandler.IsFilterPresent) return _globalVoidingLimit;

		var filter = FilterHandler.GetFilter();
		return filter.IsBlackList ? _globalVoidingLimit : filter.TestItemCount(itemStack);
	}

	public void SetVoidingMode(VoidingMode voidingMode)
	{
		_voidingMode = voidingMode;
		ConfigureFilter();
	}

	protected override void ConfigureFilter()
	{
		if (FilterHandler.GetFilter() is SimpleItemFilter filter)
			filter.SetMaxStackSize(_voidingMode.MaxStackSize());
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["voidingMode"] = (int)_voidingMode;
		tag["voidSize"] = _globalVoidingLimit;
	}

	public override void Load(TagCompound tag)
	{
		// Same Load-order trap as RobotArmCover: _voidingMode MUST be set
		// before base.Load runs the FilterHandler.Load -> ConfigureFilter chain,
		// else the default VoidAny cap=1 destructively clamps match amounts.
		if (tag.ContainsKey("voidingMode")) _voidingMode = (VoidingMode)tag.GetInt("voidingMode");
		if (tag.ContainsKey("voidSize")) _globalVoidingLimit = tag.GetInt("voidSize");
		base.Load(tag);
	}
}
