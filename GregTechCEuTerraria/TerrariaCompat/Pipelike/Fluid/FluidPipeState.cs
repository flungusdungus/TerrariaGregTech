#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Fluids.Attribute;
using GregTechCEuTerraria.Api.Transfer;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;

// Verbatim port of FluidPipeBlockEntity. Owns tanks, the 5-tick distribute
// loop, destruction checks (burn/leak/corrode/shatter/melt), receivedFrom.
//
// Adaptations: Direction (6) -> CoverSide (4 cardinal), so receivedFrom mask
// is & 15 not & 63; BlockPos -> (int x, int y); per-stack temperature reads
// FluidType.Temperature (no NBT); destroyPipe collapses to cable-burn-style
// instant destroy - see DestroyPipe for the DEVIATION rationale.
public class FluidPipeState : IFluidPipeHost
{
	public const int FREQUENCY = 5;

	public int X { get; }
	public int Y { get; }

	public byte LastReceivedFrom    = 0;
	public byte OldLastReceivedFrom = 0;

	private PipeTankList? _pipeTankList;
	private readonly Dictionary<CoverSide, PipeTankList> _tankLists = new();
	private CustomFluidTank[]? _fluidTanks;
	private long _timer = 0L;
	private readonly int _offset;

	private readonly FluidPipeProperties _nodeData;

	public FluidPipeState(int x, int y, FluidPipeProperties nodeData)
	{
		X = x;
		Y = y;
		_nodeData = nodeData;
		// Per-pipe random phase so a network doesn't fire on one tick.
		_offset = new Random(unchecked(x * 73856093 ^ y * 19349663)).Next(20);
		CreateTanksList();
	}

	public FluidPipeProperties NodeData => _nodeData;

	public long GetOffsetTimer() => _timer + _offset;

	public int CapacityPerTank => _nodeData.Throughput * 20;

	public bool IsBlocked(CoverSide side)
	{
		// Pipe-to-pipe sides are ALWAYS unblocked. Mode is meaningful only on
		// pipe-to-inventory sides (UI never exposes it on pipe-to-pipe), and
		// the default Off would WRONGLY block neighbour pipes pushing into us.
		var (dx, dy) = OffsetFor(side);
		if (FluidPipeLayerSystem.Pipes.Has(X + dx, Y + dy))
		{
			// Different-material pipes don't share a net; treat as blocked.
			var thisCell = FluidPipeLayerSystem.Pipes.CellAt(X, Y);
			var nextCell = FluidPipeLayerSystem.Pipes.CellAt(X + dx, Y + dy);
			return !(thisCell.HasValue && nextCell.HasValue &&
				thisCell.Value.MaterialId == nextCell.Value.MaterialId);
		}
		var pcv = FluidPipeLayerSystem.GetSides(X, Y);
		return pcv is null || pcv.GetMode(side) == PipeSideMode.Off;
	}

	public void ReceivedFrom(CoverSide side)
	{
		LastReceivedFrom |= (byte)(1 << (int)side);
	}

	public PipeTankList GetTankList()
	{
		if (_pipeTankList is null || _fluidTanks is null) CreateTanksList();
		return _pipeTankList!;
	}

	public PipeTankList GetTankList(CoverSide facing)
	{
		if (_tankLists.Count == 0 || _fluidTanks is null) CreateTanksList();
		return _tankLists.TryGetValue(facing, out var l) ? l : _pipeTankList!;
	}

	public CustomFluidTank[] GetFluidTanks()
	{
		if (_pipeTankList is null || _fluidTanks is null) CreateTanksList();
		return _fluidTanks!;
	}

	public FluidStack[] GetContainedFluids()
	{
		var tanks = GetFluidTanks();
		var fluids = new FluidStack[tanks.Length];
		for (int i = 0; i < fluids.Length; i++) fluids[i] = tanks[i].Fluid;
		return fluids;
	}

	public bool HasAnyFluid()
	{
		var tanks = GetFluidTanks();
		foreach (var t in tanks) if (!t.Fluid.IsEmpty) return true;
		return false;
	}

	public Terraria.ModLoader.IO.TagCompound SaveTo()
	{
		var tag = new Terraria.ModLoader.IO.TagCompound();
		var tanks = GetFluidTanks();
		var list = new System.Collections.Generic.List<Terraria.ModLoader.IO.TagCompound>(tanks.Length);
		foreach (var t in tanks) list.Add(t.SerializeNBT());
		tag["tanks"] = list;
		tag["lrf"]   = LastReceivedFrom;
		tag["olrf"]  = OldLastReceivedFrom;
		return tag;
	}

	public void LoadFrom(Terraria.ModLoader.IO.TagCompound tag)
	{
		var tanks = GetFluidTanks();
		if (tag.ContainsKey("tanks"))
		{
			var list = tag.GetList<Terraria.ModLoader.IO.TagCompound>("tanks");
			int n = Math.Min(list.Count, tanks.Length);
			for (int i = 0; i < n; i++) tanks[i].DeserializeNBT(list[i]);
		}
		if (tag.ContainsKey("lrf"))  LastReceivedFrom    = tag.GetByte("lrf");
		if (tag.ContainsKey("olrf")) OldLastReceivedFrom = tag.GetByte("olrf");
	}

	// channels = 1 / 4 / 9 (denormalised on FluidPipeCell at placement).
	private void CreateTanksList()
	{
		int channels = Math.Max(1, _nodeData.Channels);
		_fluidTanks = new CustomFluidTank[channels];
		for (int i = 0; i < channels; i++)
			_fluidTanks[i] = new CustomFluidTank(CapacityPerTank);
		_pipeTankList = new PipeTankList(this, default, _fluidTanks);
		_tankLists.Clear();
		foreach (var side in CoverSides.All)
			_tankLists[side] = new PipeTankList(this, side, _fluidTanks);
	}

	// Verbatim port of FluidPipeBlockEntity.update().
	public void Update()
	{
		_timer++;
		if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;
		// Upstream MC cadence: distribute every 5 MC ticks (= 0.25 sec at
		// SimSpeed=1.0). FROM_MC_TICKS scales to SimulationSpeed.
		if (GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(FREQUENCY) != 0) return;

		// 4-side mask (upstream 6-side & 63); sentinel = all-sides-set.
		LastReceivedFrom &= 15;
		if (LastReceivedFrom == 15)
		{
			LastReceivedFrom = 0;
		}

		bool shouldDistribute = (OldLastReceivedFrom == LastReceivedFrom);
		int tanks = _nodeData.Channels;
		int j = new Random(unchecked((int)(_timer ^ X ^ Y))).Next(Math.Max(1, tanks));
		for (int i = 0; i < tanks; i++)
		{
			int index = (i + j) % tanks;
			CustomFluidTank tank = _fluidTanks![index];
			FluidStack fluid = tank.Fluid;
			if (fluid.IsEmpty || fluid.Type is null)
				continue;
			if (fluid.Amount <= 0)
			{
				tank.SetFluid(FluidStack.Empty);
				continue;
			}

			if (shouldDistribute)
			{
				DistributeFluid(index, tank, fluid);
				LastReceivedFrom = 0;
			}
		}
		OldLastReceivedFrom = LastReceivedFrom;
	}

	// 6-side walk collapsed to 4. Two-pass shape (simulate to collect
	// targets, then real-fill by availability) preserved exactly.
	private sealed class FluidTransaction
	{
		public readonly IFluidHandler Target;
		public readonly IFluidHandler PipeTank;
		public int Amount;
		public FluidTransaction(IFluidHandler target, IFluidHandler pipeTank, int amount)
		{
			Target = target; PipeTank = pipeTank; Amount = amount;
		}
	}

	private void DistributeFluid(int channel, CustomFluidTank tank, FluidStack fluid)
	{
		var transactions = new List<FluidTransaction>();
		int amount = fluid.Amount;

		FluidStack maxFluid = fluid;
		double availableCapacity = 0;

		int sideCount = CoverSides.Count;
		int sideStart = new Random(unchecked((int)(_timer * 31 ^ X * 17 ^ Y * 13))).Next(sideCount);

		for (int i = 0; i < sideCount; i++)
		{
			byte side = (byte)((i + sideStart) % sideCount);
			CoverSide facing = (CoverSide)side;

			if (!IsConnected(facing) || (LastReceivedFrom & (1 << side)) != 0)
				continue;

			var neighborHandler = GetNeighborFluidHandler(facing);
			if (neighborHandler is null) continue;

			IFluidHandler pipeTank = tank;
			var cover = FluidPipeLayerSystem.GetSides(X, Y)?.GetCoverAtSide(facing);

			// Upstream: pipeTank determined only by the cover on the pipe.
			if (cover is not null)
			{
				pipeTank = cover.GetFluidHandlerCap(pipeTank)!;
				if (pipeTank is null || CheckForPumpCover(cover)) continue;
			}
			// Pump-cover-on-neighbour check skipped; needs WorldCapability
			// to surface the adjacent machine's coverable.

			FluidStack drainable = pipeTank.Drain(maxFluid, simulate: true);
			if (drainable.IsEmpty || drainable.Amount <= 0)
			{
				continue;
			}

			int filled = Math.Min(neighborHandler.Fill(maxFluid, simulate: true), drainable.Amount);

			if (filled > 0)
			{
				transactions.Add(new FluidTransaction(neighborHandler, pipeTank, filled));
				availableCapacity += filled;
			}
			maxFluid = maxFluid.WithAmount(amount);
		}

		if (availableCapacity <= 0) return;

		double maxAmount = Math.Min(CapacityPerTank / 2.0, fluid.Amount);

		foreach (var transaction in transactions)
		{
			if (availableCapacity > maxAmount)
			{
				transaction.Amount = (int)Math.Floor(transaction.Amount * maxAmount / availableCapacity);
			}
			if (transaction.Amount == 0)
			{
				if (tank.FluidAmount <= 0) break; // If there is no more stored fluid, stop transferring to prevent dupes.
				transaction.Amount = 1; // If the percent is not enough to give at least 1L, try to give 1L.
			}
			else if (transaction.Amount < 0)
			{
				continue;
			}

			FluidStack toInsert = fluid.WithAmount(transaction.Amount);
			if (toInsert.IsEmpty || toInsert.Type is null) continue;

			int inserted = transaction.Target.Fill(toInsert, simulate: false);
			if (inserted > 0)
			{
				transaction.PipeTank.Drain(inserted, simulate: false);
			}
		}
	}

	// Stub - PumpCover not fully ported on the fluid path yet.
	private bool CheckForPumpCover(CoverBehavior? cover) => false;

	private bool IsConnected(CoverSide facing)
	{
		// Pipe-to-pipe is IMPLICIT (never UI-gated); pipe-to-inventory respects
		// the per-side mode.
		var (dx, dy) = OffsetFor(facing);
		int nx = X + dx, ny = Y + dy;
		if (FluidPipeLayerSystem.Pipes.Has(nx, ny))
		{
			var thisCell = FluidPipeLayerSystem.Pipes.CellAt(X, Y);
			var nextCell = FluidPipeLayerSystem.Pipes.CellAt(nx, ny);
			return thisCell.HasValue && nextCell.HasValue &&
				thisCell.Value.MaterialId == nextCell.Value.MaterialId;
		}
		if (IsBlocked(facing)) return false;
		return GetNeighborFluidHandler(facing) is not null;
	}

	// Pipe-to-pipe: resolve neighbour's per-side PipeTankList directly
	// (WorldCapability.FluidHandlerAt would mode-gate, wrong for implicit
	// pipe-to-pipe connectivity). Non-pipe neighbours fall through.
	protected virtual IFluidHandler? GetNeighborFluidHandler(CoverSide facing)
	{
		var (dx, dy) = OffsetFor(facing);
		int nx = X + dx, ny = Y + dy;
		if (FluidPipeLayerSystem.Pipes.Has(nx, ny))
		{
			var neighborState = FluidPipeLayerSystem.EnsureState(nx, ny);
			return neighborState.GetTankList(OppositeSide(facing));
		}
		var arrival = ArrivalFor(facing);
		return Capabilities.WorldCapability.FluidHandlerAt(nx, ny, arrival);
	}

	private static CoverSide OppositeSide(CoverSide s) => s switch
	{
		CoverSide.Up    => CoverSide.Down,
		CoverSide.Down  => CoverSide.Up,
		CoverSide.Left  => CoverSide.Right,
		CoverSide.Right => CoverSide.Left,
		_               => s,
	};

	private static Machine.IODirection ArrivalFor(CoverSide outward) => outward switch
	{
		CoverSide.Up    => Machine.IODirection.Down,
		CoverSide.Down  => Machine.IODirection.Up,
		CoverSide.Left  => Machine.IODirection.Right,
		CoverSide.Right => Machine.IODirection.Left,
		_               => Machine.IODirection.None,
	};

	public void CheckAndDestroy(FluidStack stack)
	{
		if (stack.IsEmpty || stack.Type is null) return;
		var ft = stack.Type;
		var prop = _nodeData;

		bool burning   = prop.MaxFluidTemperature < ft.Temperature;
		bool leaking   = !prop.GasProof  && ft.Density < 0;
		bool shattering = !prop.CryoProof && ft.Temperature < FluidConstants.CRYOGENIC_FLUID_THRESHOLD;
		bool corroding = false;
		bool melting   = false;

		FluidState state = ft.State;
		if (!prop.CanContain(state))
		{
			leaking = state == FluidState.GAS;
			melting = state == FluidState.PLASMA;
		}

		// carrying plasmas which are too hot when plasma proof does not burn pipes
		if (burning && state == FluidState.PLASMA && prop.CanContain(FluidState.PLASMA))
		{
			burning = false;
		}

		foreach (FluidAttribute attribute in ft.Attributes)
		{
			if (!prop.CanContain(attribute))
			{
				// corrodes if the pipe can't handle the attribute, even if it's not an acid
				corroding = true;
			}
		}

		if (burning || leaking || corroding || shattering || melting)
		{
			DestroyPipe(stack, burning, leaking, corroding, shattering, melting);
		}
	}

	// DEVIATION: cable-burn-style INSTANT destroy
	// on first contact with an incompatible fluid (vs upstream's gradual void
	// + 1/10 finaliser). Per-tick voiding, entity damage, FIRE placement and
	// mini-explosions all dropped - revisit each as a separate ask.
	public void DestroyPipe(FluidStack stack, bool isBurning, bool isLeaking, bool isCorroding,
	                        bool isShattering, bool isMelting)
	{
		if (_destroyed) return;
		_destroyed = true;

		Net.BlockExplosionEffectPacket.PlayLocal(X, Y, 1, 1);
		if (Main.netMode == Terraria.ID.NetmodeID.Server)
			Net.BlockExplosionEffectPacket.Send(X, Y, 1, 1);

		if (Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient)
		{
			var cell = FluidPipeLayerSystem.Pipes.CellAt(X, Y);
			if (cell.HasValue)
			{
				string id = cell.Value.MaterialId + "_" + PipeSizes.Word(cell.Value.Size) + "_fluid_pipe";
				int? itemType = Items.Pipes.PipeItemRegistry.Get(id);
				if (itemType is not null)
					Terraria.Item.NewItem(new Terraria.DataStructures.EntitySource_TileBreak(X, Y),
						X * 16, Y * 16, 16, 16, itemType.Value);
			}
		}

		FluidPipeLayerSystem.Pipes.Remove(X, Y);
		FluidPipeLayerSystem.DropSides(X, Y);
		FluidPipeNetSystem.OnPipeRemoved(X, Y);
		FluidPipeLayerHandle.NotifyAdjacentCoversNeighborChanged(X, Y);
		Net.PipePackets.SendRemove(X, Y, PipeKind.Fluid);
	}

	// Guards against re-entry on multi-fill ticks; persisted so save/load
	// doesn't re-fire the effect.
	private bool _destroyed;

	private static (int dx, int dy) OffsetFor(CoverSide side) => side switch
	{
		CoverSide.Up    => (0, -1),
		CoverSide.Down  => (0, +1),
		CoverSide.Left  => (-1, 0),
		CoverSide.Right => (+1, 0),
		_               => (0, 0),
	};

}
