#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Fluids;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.FluidRegulatorCover - fluid twin of RobotArmCover.
// PumpCover + TransferAny / TransferExact / KeepExact. Adaptations: LDLib
// createUIWidget + copyConfig dropped; `transferBucketMode` dropped - upstream
// uses it only as the limit-widget display unit, our field shows raw mB.
public class FluidRegulatorCover : PumpCover, ITransferModeCover
{
	private const int MaxStackSize = 2_048_000_000;   // Quantum Tank IX capacity

	private TransferMode _transferMode = TransferMode.TransferAny;
	private int _globalTransferLimit;
	private int _fluidTransferBuffered;

	public FluidRegulatorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide, int tier)
		: base(definition, coverHolder, attachedSide, tier) { }

	public TransferMode TransferMode => _transferMode;
	public int GlobalTransferLimit => _globalTransferLimit;

	public void SetTransferMode(TransferMode transferMode)
	{
		_transferMode = transferMode;
		ConfigureFilter();
	}

	public void SetGlobalTransferLimit(int limit) =>
		_globalTransferLimit = Math.Clamp(limit, 0, MaxStackSize);

	// field 5=transfer mode, 6=per-type limit. 0-4 fall through to PumpCover.
	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 5: SetTransferMode((TransferMode)Math.Clamp(value, 0, 2)); break;
			case 6: SetGlobalTransferLimit((int)Math.Clamp(value, 0, MaxStackSize)); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	protected override int DoTransferFluidsInternal(IFluidHandler source, IFluidHandler destination,
		int platformTransferLimit) =>
		_transferMode switch
		{
			TransferMode.TransferAny   => TransferAny(source, destination, platformTransferLimit),
			TransferMode.TransferExact => TransferExact(source, destination, platformTransferLimit),
			TransferMode.KeepExact     => KeepExact(source, destination, platformTransferLimit),
			_                          => 0,
		};

	private int TransferExact(IFluidHandler source, IFluidHandler destination, int platformTransferLimit)
	{
		int fluidLeftToTransfer = platformTransferLimit;

		for (int slot = 0; slot < source.TankCount; slot++)
		{
			if (fluidLeftToTransfer <= 0) break;

			FluidStack sourceFluid = source.GetTank(slot);
			int supplyAmount = GetFilteredFluidAmount(sourceFluid);

			// Insufficient budget - buffer for next cycle.
			if (fluidLeftToTransfer + _fluidTransferBuffered < supplyAmount)
			{
				_fluidTransferBuffered += fluidLeftToTransfer;
				fluidLeftToTransfer = 0;
				break;
			}

			if (sourceFluid.IsEmpty || supplyAmount <= 0) continue;

			FluidStack drained = source.Drain(sourceFluid.WithAmount(supplyAmount), simulate: true);
			if (drained.IsEmpty || drained.Amount < supplyAmount) continue;

			int insertableAmount = destination.Fill(drained, simulate: true);
			if (insertableAmount != supplyAmount) continue;

			drained = source.Drain(drained.WithAmount(insertableAmount), simulate: false);
			if (!drained.IsEmpty)
			{
				destination.Fill(drained, simulate: false);
				fluidLeftToTransfer -= (drained.Amount - _fluidTransferBuffered);
			}
			_fluidTransferBuffered = 0;
		}

		return platformTransferLimit - fluidLeftToTransfer;
	}

	private int KeepExact(IFluidHandler source, IFluidHandler destination, int platformTransferLimit)
	{
		int fluidLeftToTransfer = platformTransferLimit;

		var sourceAmounts = EnumerateDistinctFluids(source, TransferDirection.Extract);
		var destinationAmounts = EnumerateDistinctFluids(destination, TransferDirection.Insert);

		foreach (FluidStack fluidStack in sourceAmounts.Keys)
		{
			if (fluidLeftToTransfer <= 0) break;

			int amountToKeep = GetFilteredFluidAmount(fluidStack);
			long amountInDest = destinationAmounts.TryGetValue(fluidStack, out var v) ? v : 0;
			if (amountInDest >= amountToKeep) continue;

			FluidStack fluidToMove = fluidStack.WithAmount(
				(int)Math.Min(fluidLeftToTransfer, amountToKeep - amountInDest));
			if (fluidToMove.Amount <= 0) continue;

			FluidStack drained = source.Drain(fluidToMove, simulate: true);
			int fillableAmount = destination.Fill(drained, simulate: true);
			if (fillableAmount <= 0) continue;

			fluidToMove = fluidToMove.WithAmount(Math.Min(fluidToMove.Amount, fillableAmount));
			drained = source.Drain(fluidToMove, simulate: false);
			int movedAmount = destination.Fill(drained, simulate: false);
			fluidLeftToTransfer -= movedAmount;
		}

		return platformTransferLimit - fluidLeftToTransfer;
	}

	private int GetFilteredFluidAmount(FluidStack fluidStack)
	{
		if (!FilterHandler.IsFilterPresent) return _globalTransferLimit;
		var filter = FilterHandler.GetFilter();
		return filter.SupportsAmounts ? filter.TestFluidAmount(fluidStack) : _globalTransferLimit;
	}

	protected override void ConfigureFilter()
	{
		if (FilterHandler.GetFilter() is SimpleFluidFilter filter)
			filter.SetMaxStackSize(_transferMode == TransferMode.TransferAny ? 1 : MaxStackSize);
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["transferMode"] = (int)_transferMode;
		tag["transferLimit"] = _globalTransferLimit;
	}

	public override void Load(TagCompound tag)
	{
		// Same Load-order trap as RobotArmCover: _transferMode MUST be set
		// before base.Load runs the FilterHandler.Load -> ConfigureFilter chain,
		// else the default Any-mode cap=1 destructively clamps match amounts.
		if (tag.ContainsKey("transferMode")) _transferMode = (TransferMode)tag.GetInt("transferMode");
		if (tag.ContainsKey("transferLimit")) _globalTransferLimit = tag.GetInt("transferLimit");
		base.Load(tag);
	}
}
