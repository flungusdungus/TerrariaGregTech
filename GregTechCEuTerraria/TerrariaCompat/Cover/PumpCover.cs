#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Transfer;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.PumpCover. Moves fluid host<->adjacent, filter-gated.
// Same adaptation shape as ConveyorCover (LDLib / tool hooks / copy/paste
// dropped; WorldCapability replaces Forge-cap lookup; BucketMode is a plain
// persisted field). transferAny + GTTransferUtils.transferFluidsFiltered +
// FluidUtil.tryFluidTransfer are reproduced inline.
public class PumpCover : CoverBehavior, IIOCover, IUICover, IControllable
{
	// Verbatim PUMP_SCALING: .5b 2b 8b ... = 64 x 4^min(tier-1, IV).
	public static int PumpScaling(int tier) =>
		64 * (int)Math.Pow(4, Math.Min(tier - 1, (int)VoltageTier.IV));

	public readonly int Tier;
	public readonly int MaxFluidTransferRate;

	public int TransferRate { get; protected set; }
	public IO Io { get; protected set; } = IO.OUT;
	public BucketMode BucketMode { get; protected set; } = BucketMode.MilliBucket;
	public ManualIOMode ManualIOMode { get; protected set; } = ManualIOMode.Disabled;

	protected bool _isWorkingEnabled = true;
	protected int _mBLeftToTransferLastSecond;

	protected readonly FluidFilterHandler FilterHandler;
	protected readonly ConditionalSubscriptionHandler SubscriptionHandler;

	// Exposes the filter handler to the settings popup. Inherited by
	// FluidVoidingCover / Advanced*.
	public override FluidFilterHandler? UiFluidFilterHandler => FilterHandler;

	public PumpCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide,
		int tier, int maxTransferRate)
		: base(definition, coverHolder, attachedSide)
	{
		Tier = tier;
		MaxFluidTransferRate = maxTransferRate;
		TransferRate = maxTransferRate;
		_mBLeftToTransferLastSecond = TransferRate * 20;

		SubscriptionHandler = new ConditionalSubscriptionHandler(coverHolder, Update, IsSubscriptionActive);
		FilterHandler = FilterHandlers.Fluid(this);
		FilterHandler.WithFilterLoaded(ConfigureFilter)
			.WithFilterUpdated(ConfigureFilter)
			.WithFilterRemoved(ConfigureFilter);
	}

	public PumpCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide, int tier)
		: this(definition, coverHolder, attachedSide, tier, PumpScaling(tier)) { }

	protected virtual bool IsSubscriptionActive() =>
		IsWorkingEnabled() && GetAdjacentFluidHandler() != null;

	// Fluid sibling of ConveyorCover.GetOwnItemHandler.
	protected IFluidHandler? GetOwnFluidHandler()
	{
		var side = WorldCapability.ToIODirection(AttachedSide);
		if (CoverHolder is MetaMachine m)
			return m.GetFluidHandlerCap(side, useCoverCapability: false);
		if (CoverHolder is TerrariaCompat.Pipelike.PipeCoverable pcv)
			return WorldCapability.FluidHandlerAt(pcv.X, pcv.Y, side);
		return null;
	}

	// Equivalent of upstream's Forge FLUID_HANDLER capability lookup at
	// pos.relative(side).
	protected IFluidHandler? GetAdjacentFluidHandler()
	{
		var dir = WorldCapability.ToIODirection(AttachedSide);

		if (CoverHolder is MetaMachine machine)
		{
			var own = GetOwnFluidHandler();
			foreach (var (side, x, y) in WorldCapability.Perimeter(machine))
			{
				if (side != dir) continue;
				var handler = WorldCapability.FluidHandlerAt(x, y, side.Opposite());
				if (handler != null && !ReferenceEquals(handler, own))
					return handler;
			}
			return null;
		}

		if (CoverHolder is TerrariaCompat.Pipelike.PipeCoverable pcv)
		{
			var (dx, dy) = dir.Offset();
			return WorldCapability.FluidHandlerAt(pcv.X + dx, pcv.Y + dy, dir.Opposite());
		}

		return null;
	}

	public override bool CanAttach() => base.CanAttach() && GetOwnFluidHandler() != null;

	public void SetIo(IO io)
	{
		if (io is IO.IN or IO.OUT) Io = io;
	}

	public override void OnLoad()
	{
		base.OnLoad();
		SubscriptionHandler.Initialize();
	}

	public override void OnRemoved()
	{
		base.OnRemoved();
		SubscriptionHandler.Unsubscribe();
	}

	public override List<Item> GetAdditionalDrops()
	{
		var list = base.GetAdditionalDrops();
		if (!FilterHandler.FilterItem.IsAir) list.Add(FilterHandler.FilterItem);
		return list;
	}

	public override void OnNeighborChanged() => SubscriptionHandler.UpdateSubscription();

	// === IControllable ======================================================

	public bool IsWorkingEnabled() => _isWorkingEnabled;

	public void SetWorkingEnabled(bool isWorkingAllowed)
	{
		if (_isWorkingEnabled != isWorkingAllowed)
		{
			_isWorkingEnabled = isWorkingAllowed;
			SubscriptionHandler.UpdateSubscription();
		}
	}

	public void SetTransferRate(int milliBucketsPerTick) =>
		TransferRate = Math.Min(Math.Max(milliBucketsPerTick, 0), MaxFluidTransferRate);

	public void SetBucketMode(BucketMode bucketMode) => BucketMode = bucketMode;

	protected void SetManualIOMode(ManualIOMode manualIOMode) => ManualIOMode = manualIOMode;

	// field 1=IO, 2=manual-IO mode, 3=transfer rate (mB/t), 4=bucket mode.
	// Field 0 (working-enabled) falls through to base.
	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 1: SetIo((IO)value); break;
			case 2: SetManualIOMode((ManualIOMode)Math.Clamp(value, 0, 2)); break;
			case 3: SetTransferRate((int)value); break;
			case 4: SetBucketMode((BucketMode)Math.Clamp(value, 0, 1)); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	protected virtual void Update()
	{
		// Active-cover identity gate - mirrors ConveyorCover.Update.
		if (!ReferenceEquals(((ICoverable)CoverHolder).GetCoverAtSide(AttachedSide), this))
			return;

		long timer = CoverHolder.GetOffsetTimer();
		if (timer % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(5) != 0) return;

		if (_mBLeftToTransferLastSecond > 0)
		{
			int transferred = DoTransferFluids(_mBLeftToTransferLastSecond);
			_mBLeftToTransferLastSecond -= transferred;
		}

		if (timer % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) == 0)
			_mBLeftToTransferLastSecond = TransferRate * 20;

		SubscriptionHandler.UpdateSubscription();
	}

	protected virtual int DoTransferFluids(int platformTransferLimit)
	{
		var adjacent = GetAdjacentFluidHandler();
		var own = GetOwnFluidHandler();
		if (adjacent == null || own == null) return 0;

		return Io switch
		{
			IO.IN => DoTransferFluidsInternal(adjacent, own, platformTransferLimit),
			IO.OUT => DoTransferFluidsInternal(own, adjacent, platformTransferLimit),
			_ => 0,
		};
	}

	protected virtual int DoTransferFluidsInternal(IFluidHandler source, IFluidHandler destination,
		int platformTransferLimit) =>
		TransferAny(source, destination, platformTransferLimit);

	protected int TransferAny(IFluidHandler source, IFluidHandler destination, int platformTransferLimit) =>
		TransferFluidsFiltered(source, destination, FilterHandler.GetFilter(), platformTransferLimit);

	// Verbatim GTTransferUtils.transferFluidsFiltered.
	protected static int TransferFluidsFiltered(IFluidHandler source, IFluidHandler dest,
		IFluidFilter filter, int transferLimit)
	{
		int toTransfer = transferLimit;
		for (int i = 0; i < source.TankCount; i++)
		{
			var fluid = source.GetTank(i);
			if (fluid.IsEmpty || !filter.Test(fluid)) continue;

			int transferred = TryFluidTransfer(dest, source, fluid.WithAmount(toTransfer));
			toTransfer -= transferred;
			if (toTransfer <= 0) break;
		}
		return transferLimit - toTransfer;
	}

	// Verbatim Forge FluidUtil.tryFluidTransfer: simulate drain + fill, commit min.
	private static int TryFluidTransfer(IFluidHandler dest, IFluidHandler source, FluidStack resource)
	{
		if (resource.IsEmpty) return 0;
		var drainable = source.Drain(resource, simulate: true);
		if (drainable.IsEmpty) return 0;
		int fillable = dest.Fill(drainable, simulate: true);
		if (fillable <= 0) return 0;
		var actuallyDrained = source.Drain(drainable.WithAmount(fillable), simulate: false);
		if (actuallyDrained.IsEmpty) return 0;
		return dest.Fill(actuallyDrained, simulate: false);
	}

	protected enum TransferDirection
	{
		Insert,
		Extract,
	}

	// Sum per tank, keyed by normalised amount-1 FluidStack. Upstream gates by
	// per-tank supportsFill/supportsDrain; our IFluidHandler is whole-handler
	// IO-gated, so every tank counts for voiding covers.
	protected Dictionary<FluidStack, long> EnumerateDistinctFluids(IFluidHandler fluidHandler, TransferDirection direction)
	{
		_ = direction;
		var summedFluids = new Dictionary<FluidStack, long>();
		for (int tank = 0; tank < fluidHandler.TankCount; tank++)
		{
			FluidStack fluidStack = fluidHandler.GetTank(tank);
			if (fluidStack.IsEmpty) continue;

			var key = fluidStack.WithAmount(1);
			summedFluids.TryGetValue(key, out long current);
			summedFluids[key] = current + fluidStack.Amount;
		}
		return summedFluids;
	}

	// AdvancedFluidVoidingCover overrides to clamp the filter's max stack.
	protected virtual void ConfigureFilter() { }

	// Capability override - pipe-consulted.

	private CoverableFluidHandlerWrapper? _fluidHandlerWrapper;

	public override IFluidHandler? GetFluidHandlerCap(IFluidHandler? defaultValue)
	{
		if (defaultValue == null) return null;
		if (_fluidHandlerWrapper == null || _fluidHandlerWrapper.Inner != defaultValue)
			_fluidHandlerWrapper = new CoverableFluidHandlerWrapper(this, defaultValue);
		return _fluidHandlerWrapper;
	}

	private sealed class CoverableFluidHandlerWrapper : FluidHandlerDelegate
	{
		private readonly PumpCover _cover;

		public CoverableFluidHandlerWrapper(PumpCover cover, IFluidHandler inner) : base(inner) => _cover = cover;

		public override int Fill(FluidStack resource, bool simulate)
		{
			if (_cover.Io == IO.OUT)
			{
				if (_cover.ManualIOMode == ManualIOMode.Disabled) return 0;
				if (_cover.ManualIOMode == ManualIOMode.Unfiltered) return base.Fill(resource, simulate);
			}
			if (!_cover.FilterHandler.Test(resource)) return 0;
			return base.Fill(resource, simulate);
		}

		public override FluidStack Drain(FluidStack resource, bool simulate)
		{
			if (_cover.Io == IO.IN)
			{
				if (_cover.ManualIOMode == ManualIOMode.Disabled) return FluidStack.Empty;
				if (_cover.ManualIOMode == ManualIOMode.Unfiltered) return base.Drain(resource, simulate);
			}
			if (!_cover.FilterHandler.Test(resource)) return FluidStack.Empty;
			return base.Drain(resource, simulate);
		}
	}

	// === Persistence ========================================================

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["transferRate"] = TransferRate;
		tag["io"] = (int)Io;
		tag["manualIO"] = (int)ManualIOMode;
		tag["bucketMode"] = (int)BucketMode;
		tag["workingEnabled"] = _isWorkingEnabled;
		var filterTag = new TagCompound();
		FilterHandler.Save(filterTag);
		tag["filter"] = filterTag;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("transferRate")) TransferRate = tag.GetInt("transferRate");
		if (tag.ContainsKey("io")) Io = (IO)tag.GetInt("io");
		if (tag.ContainsKey("manualIO")) ManualIOMode = (ManualIOMode)tag.GetInt("manualIO");
		if (tag.ContainsKey("bucketMode")) BucketMode = (BucketMode)tag.GetInt("bucketMode");
		if (tag.ContainsKey("workingEnabled")) _isWorkingEnabled = tag.GetBool("workingEnabled");
		if (tag.ContainsKey("filter")) FilterHandler.Load(tag.GetCompound("filter"));
	}
}
