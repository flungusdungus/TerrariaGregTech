#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Fluids;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Voiding;

// Port of common.cover.voiding.AdvancedFluidVoidingCover. Fluid mirror of
// AdvancedItemVoidingCover. transferBucketMode kept persisted for save-compat.
public class AdvancedFluidVoidingCover : FluidVoidingCover, IAdvancedVoidingCover
{
	private VoidingMode _voidingMode = VoidingMode.VoidAny;
	private int _globalTransferSizeMillibuckets = 1;
	private BucketMode _transferBucketMode = BucketMode.MilliBucket;

	public AdvancedFluidVoidingCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public VoidingMode VoidingMode => _voidingMode;
	public int GlobalTransferSizeMillibuckets => _globalTransferSizeMillibuckets;
	public int VoidLimit => _globalTransferSizeMillibuckets;

	// field 1=voiding mode, 2=per-type mB overflow limit. 0 falls through.
	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 1: SetVoidingMode((VoidingMode)System.Math.Clamp(value, 0, 1)); break;
			case 2: _globalTransferSizeMillibuckets = (int)System.Math.Clamp(value, 0, 1_000_000); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	protected override void DoVoidFluids()
	{
		var fluidHandler = GetOwnFluidHandler();
		if (fluidHandler == null) return;

		switch (_voidingMode)
		{
			case VoidingMode.VoidAny:
				VoidAny(fluidHandler);
				break;
			case VoidingMode.VoidOverflow:
				VoidOverflow(fluidHandler);
				break;
		}
	}

	private void VoidOverflow(IFluidHandler fluidHandler)
	{
		var fluidAmounts = EnumerateDistinctFluids(fluidHandler, TransferDirection.Extract);

		foreach (var entry in fluidAmounts)
		{
			var stack = entry.Key;
			long presentAmount = entry.Value;
			int targetAmount = GetFilteredFluidAmount(stack);
			if (targetAmount <= 0L || targetAmount > presentAmount) continue;

			long diff = presentAmount - targetAmount;
			foreach (int op in Split(diff))
				fluidHandler.Drain(stack.WithAmount(op), simulate: false);
		}
	}

	private int GetFilteredFluidAmount(FluidStack fluidStack)
	{
		if (!FilterHandler.IsFilterPresent) return _globalTransferSizeMillibuckets;

		var filter = FilterHandler.GetFilter();
		return filter.IsBlackList ? _globalTransferSizeMillibuckets : filter.TestFluidAmount(fluidStack);
	}

	public void SetVoidingMode(VoidingMode voidingMode)
	{
		_voidingMode = voidingMode;
		ConfigureFilter();
	}

	protected override void ConfigureFilter()
	{
		if (FilterHandler.GetFilter() is SimpleFluidFilter filter)
			filter.SetMaxStackSize(_voidingMode == VoidingMode.VoidAny ? 1 : int.MaxValue);
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["voidingMode"] = (int)_voidingMode;
		tag["voidSize"] = _globalTransferSizeMillibuckets;
		tag["voidBucketMode"] = (int)_transferBucketMode;
	}

	public override void Load(TagCompound tag)
	{
		// Load-order trap (see RobotArmCover): _voidingMode before base.Load.
		if (tag.ContainsKey("voidingMode")) _voidingMode = (VoidingMode)tag.GetInt("voidingMode");
		if (tag.ContainsKey("voidSize")) _globalTransferSizeMillibuckets = tag.GetInt("voidSize");
		if (tag.ContainsKey("voidBucketMode")) _transferBucketMode = (BucketMode)tag.GetInt("voidBucketMode");
		base.Load(tag);
	}
}
