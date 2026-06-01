#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.TerrariaCompat.Cover;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike;

// Per-pipe-cell ICoverable (analogue of upstream's PipeCoverContainer).
// Per-side state, three independent fields:
//   _modes[i]        - Off / Passive / Active : which cover is in effect
//   _filterTypes[i]  - None / Simple / Tag    : pipe-UI filter choice
//   _filterCovers[i] - Passive cover (ItemFilterCover / FluidFilterCover)
//   _robotArms[i]    - Active cover  (RobotArmCover / FluidRegulatorCover)
//
// DEVIATION: upstream pipes have no Passive /
// Active mode; covers are placed individually by item. The two-slot
// container + mode dispatch is pipe-UI mechanism. The cover BEHAVIOURS
// themselves stay upstream-verbatim.
public sealed class PipeCoverable : ICoverable
{
	public PipeKind Layer { get; }
	public int X { get; }
	public int Y { get; }

	public enum PipeFilterType { None = 0, Simple = 1, Tag = 2 }

	private readonly PipeSideMode[] _modes = new PipeSideMode[CoverSides.Count];

	// Mode-independent. Default Simple so a fresh side toggled into Passive
	// auto-installs the Simple filter cover.
	private readonly PipeFilterType[] _filterTypes = InitFilterTypes();
	private static PipeFilterType[] InitFilterTypes()
	{
		var arr = new PipeFilterType[CoverSides.Count];
		for (int i = 0; i < arr.Length; i++) arr[i] = PipeFilterType.Simple;
		return arr;
	}

	// Lazy-allocated. SetFilterType(None) clears the Passive slot while
	// keeping _modes[i] = Passive (side is "configured as transparent").
	// Mode flip keeps the inactive side's cover so settings survive.
	//   Item  layer: ItemFilterCover  / RobotArmCover
	//   Fluid layer: FluidFilterCover / FluidRegulatorCover
	internal readonly CoverBehavior?[] _filterCovers = new CoverBehavior?[CoverSides.Count];
	internal readonly CoverBehavior?[] _robotArms    = new CoverBehavior?[CoverSides.Count];

	// Per-pipe tick state (upstream ItemPipeBlockEntity's transferredItems +
	// transferred map). TransferredItems is the 20-tick throughput counter
	// with a LAZY reset (verbatim with upstream updateTransferredState).
	private int _transferredItems;
	private long _transferredTimer;
	public int TransferredItems
	{
		get { UpdateTransferredState(); return _transferredItems; }
		set { UpdateTransferredState(); _transferredItems = value; }
	}
	public readonly System.Collections.Generic.Dictionary<(int x, int y, CoverSide f), int> Transferred = new();

	private void UpdateTransferredState()
	{
		// Upstream window = 20 MC ticks; FromMcTicks scales to SimulationSpeed.
		int window = Api.TickScale.FromMcTicks(20);
		long now = Main.GameUpdateCount;
		long dif = now - _transferredTimer;
		if (dif >= window || dif < 0)
		{
			_transferredItems = 0;
			_transferredTimer = now;
		}
	}

	public void ResetTransferred() => Transferred.Clear();

	// Lazy ItemNetHandler per side; same instance accumulates per-tick state
	// across inserts. Reset on network rebuild.
	internal readonly object?[] CachedItemHandlers = new object?[CoverSides.Count];

	public PipeCoverable(PipeKind layer, int x, int y)
	{
		Layer = layer;
		X = x;
		Y = y;
	}

	public Point16 GetBlockPos() => new(X, Y);

	public bool IsRemote => Main.netMode == NetmodeID.MultiplayerClient;

	public void NotifyBlockUpdate()
	{
		// Pipes have no per-cell cover render today; hook here if one appears.
	}

	// Verbatim port of MetaMachine's server-tick subscription pattern.
	private readonly System.Collections.Generic.List<TickableSubscription> _serverTicks  = new();
	private readonly System.Collections.Generic.List<TickableSubscription> _waitingToAdd = new();

	public TickableSubscription? SubscribeServerTick(Action runnable)
	{
		if (IsRemote) return null;
		var sub = new TickableSubscription(runnable);
		_waitingToAdd.Add(sub);
		return sub;
	}

	public void Unsubscribe(TickableSubscription? subscription) => subscription?.Unsubscribe();

	internal void SystemTick()
	{
		if (IsRemote) return;
		if (_waitingToAdd.Count > 0)
		{
			_serverTicks.AddRange(_waitingToAdd);
			_waitingToAdd.Clear();
		}
		for (int i = _serverTicks.Count - 1; i >= 0; i--)
		{
			var sub = _serverTicks[i];
			if (sub.StillSubscribed) sub.Run();
			if (!sub.StillSubscribed) _serverTicks.RemoveAt(i);
		}
	}

	public long GetOffsetTimer() => Main.GameUpdateCount;

	public PipeSideMode GetMode(CoverSide side) => _modes[(int)side];

	public void SetMode(CoverSide side, PipeSideMode mode)
	{
		int i = (int)side;
		var oldMode = _modes[i];
		if (oldMode == mode) return;

		// Lazy-create destination cover BEFORE copy so its filter is available.
		switch (mode)
		{
			case PipeSideMode.Passive: EnsureFilterCover(side); break;
			case PipeSideMode.Active:  EnsureRobotArm(side);    break;
		}

		CopyFilterStateOnModeChange(side, oldMode, mode);

		_modes[i] = mode;

		// Route caches on ItemPipeNet are per-source-pipe and would otherwise
		// keep delivering through a side the player just turned Off.
		InvalidateNetRouteCache();

		// Make the UI toggle feel immediate (otherwise ~20-tick lag while
		// SubscriptionHandler re-polls).
		GetCoverAtSide(side)?.OnNeighborChanged();

		NotifyBlockUpdate();
	}

	// Layer-aware so the fluid net can wire in the same way once ported.
	private void InvalidateNetRouteCache()
	{
		if (Layer != PipeKind.Item) return;
		var net = ItemPipe.ItemPipeNetSystem.Level.GetNetFromPos((X, Y));
		net?.OnNeighbourUpdate((X, Y));
	}

	private void CopyFilterStateOnModeChange(CoverSide side, PipeSideMode from, PipeSideMode to)
	{
		if (from == to) return;
		if (from == PipeSideMode.Off || to == PipeSideMode.Off) return;

		int i = (int)side;
		// Passive + Active both expose UiItemFilter / UiFluidFilter; copy at
		// that level via SaveFilter / LoadFrom. Simple<->Simple preserves
		// contents; Tag-filter shape doesn't survive cross-mode copy.
		if (Layer == PipeKind.Item)
		{
			var src = from switch
			{
				PipeSideMode.Passive => _filterCovers[i]?.UiItemFilter,
				PipeSideMode.Active  => _robotArms   [i]?.UiItemFilter,
				_ => null,
			};
			var dst = to switch
			{
				PipeSideMode.Passive => _filterCovers[i]?.UiItemFilter,
				PipeSideMode.Active  => _robotArms   [i]?.UiItemFilter,
				_ => null,
			};
			if (src is null || dst is null || ReferenceEquals(src, dst)) return;
			var blob = src.SaveFilter();
			if (blob is null) dst.Reset();
			else              dst.LoadFrom(blob);
		}
		else if (Layer == PipeKind.Fluid)
		{
			var src = from switch
			{
				PipeSideMode.Passive => _filterCovers[i]?.UiFluidFilter,
				PipeSideMode.Active  => _robotArms   [i]?.UiFluidFilter,
				_ => null,
			};
			var dst = to switch
			{
				PipeSideMode.Passive => _filterCovers[i]?.UiFluidFilter,
				PipeSideMode.Active  => _robotArms   [i]?.UiFluidFilter,
				_ => null,
			};
			if (src is null || dst is null || ReferenceEquals(src, dst)) return;
			var blob = src.SaveFilter();
			if (blob is null) dst.Reset();
			else              dst.LoadFrom(blob);
		}
	}

	// Single Io source for the renderer + settings panel + any other consumer.
	internal static Api.Capability.Recipe.IO? ActiveIoAt(ICoverable holder, CoverSide side) => holder.GetCoverAtSide(side) switch
	{
		RobotArmCover r        => r.Io,
		FluidRegulatorCover f  => f.Io,
		_                      => null,
	};

	// Simple-pipe UI: OFF/INSERT/EXTRACT maps to (mode, cover.Io, filter).
	// A simple-mode side is just Active + RobotArm/FluidRegulator with a
	// fixed allow-all filter (Blacklist + no matches).

	public SimpleSideMode GetSimpleMode(CoverSide side)
	{
		int i = (int)side;
		if (_modes[i] == PipeSideMode.Off) return SimpleSideMode.Off;
		if (_modes[i] != PipeSideMode.Active) return SimpleSideMode.Off;
		return ActiveIoAt(this, side) switch
		{
			Api.Capability.Recipe.IO.OUT => SimpleSideMode.Insert,
			Api.Capability.Recipe.IO.IN  => SimpleSideMode.Extract,
			_                            => SimpleSideMode.Off,
		};
	}

	public void SetSimpleMode(CoverSide side, SimpleSideMode mode)
	{
		if (mode == SimpleSideMode.Off)
		{
			SetMode(side, PipeSideMode.Off);
			return;
		}

		SetMode(side, PipeSideMode.Active);

		// Simple filter + blacklist + no matches = allow-all.
		SetFilterType(side, PipeFilterType.Simple);
		var cover = GetCoverAtSide(side);
		if (Layer == PipeKind.Item)
			cover?.UiItemFilter?.SetBlackList(true);
		else
			cover?.UiFluidFilter?.SetBlackList(true);

		var targetIo = mode == SimpleSideMode.Insert
			? Api.Capability.Recipe.IO.OUT   // push to adjacent
			: Api.Capability.Recipe.IO.IN;   // pull from adjacent
		switch (cover)
		{
			case RobotArmCover ra        when ra.Io != targetIo: ra.SetIo(targetIo); break;
			case FluidRegulatorCover fr  when fr.Io != targetIo: fr.SetIo(targetIo); break;
		}
		NotifyBlockUpdate();
	}

	public CoverBehavior? GetCoverAtSide(CoverSide side) => _modes[(int)side] switch
	{
		PipeSideMode.Passive => _filterCovers[(int)side],
		PipeSideMode.Active  => _robotArms   [(int)side],
		_                    => null,
	};

	// Override default - a side with mode != Off but no live cover (Passive +
	// filterType=None) is still meaningful state; don't let the server evict us.
	bool ICoverable.HasAnyCover()
	{
		for (int i = 0; i < CoverSides.Count; i++)
		{
			if (_filterCovers[i] is not null) return true;
			if (_robotArms[i]    is not null) return true;
			if (_modes[i] != PipeSideMode.Off) return true;
		}
		return false;
	}

	// ICoverable contract. Mode is NOT set here - LoadCovers reads it itself.
	// External callers should go through SetMode + Ensure* + cover.ApplySetting.
	public void SetCoverAtSide(CoverBehavior? cover, CoverSide side)
	{
		int i = (int)side;
		switch (cover)
		{
			case null:
				_modes[i]        = PipeSideMode.Off;
				_filterCovers[i] = null;
				_robotArms[i]    = null;
				break;
			case RobotArmCover:
			case FluidRegulatorCover:
				_robotArms[i] = cover;
				break;
			case ItemFilterCover:
			case FluidFilterCover:
				_filterCovers[i] = cover;
				break;
		}
		NotifyBlockUpdate();
	}

	// Fresh side starts in empty-whitelist (blocks all). Subsequent mode
	// toggles preserve player config via Save/Load in CopyFilterStateOnModeChange.
	private CoverBehavior? EnsureFilterCover(CoverSide side)
	{
		int i = (int)side;
		if (_filterCovers[i] is null)
			InstallFilterCover(side, _filterTypes[i]);
		return _filterCovers[i];
	}

	private CoverBehavior? EnsureRobotArm(CoverSide side)
	{
		int i = (int)side;
		if (_robotArms[i] is not null) return _robotArms[i];
		// LuV: ConveyorScaling/PumpScaling caps at LuV (1024 items/s, 64 buckets/s),
		// well above any pipe's per-second cap - the pipe is the bottleneck.
		string registryId = Layer == PipeKind.Item ? "robot_arm.luv" : "fluid_regulator.luv";
		var def = CoverRegistry.Get(registryId);
		if (def is null) return null;
		var cover = def.CreateCoverBehavior(this, side);
		cover.OnAttached(new Item());
		cover.OnLoad();
		_robotArms[i] = cover;
		ApplyActiveFilterItem(side, _filterTypes[i]);
		return cover;
	}

	internal PipeFilterType GetFilterType(CoverSide side) => _filterTypes[(int)side];

	internal void SetFilterType(CoverSide side, PipeFilterType type)
	{
		int i = (int)side;
		_filterTypes[i] = type;
		switch (_modes[i])
		{
			case PipeSideMode.Passive: InstallFilterCover(side, type); break;
			case PipeSideMode.Active:  ApplyActiveFilterItem(side, type); break;
			// Off keeps the stored type; applied when player next picks a mode.
		}
		NotifyBlockUpdate();
	}

	// Upstream cover-item id used as the fresh cover's AttachItem.
	private string? FilterCoverItemId(PipeFilterType type) => (Layer, type) switch
	{
		(PipeKind.Item , PipeFilterType.Simple) => "gtceu:item_filter",
		(PipeKind.Item , PipeFilterType.Tag)    => "gtceu:item_tag_filter",
		(PipeKind.Fluid, PipeFilterType.Simple) => "gtceu:fluid_filter",
		(PipeKind.Fluid, PipeFilterType.Tag)    => "gtceu:fluid_tag_filter",
		_                                       => null,
	};

	private int ResolveFilterItemId(PipeFilterType type)
	{
		string? upstreamId = FilterCoverItemId(type);
		if (upstreamId is null) return 0;
		if (Items.Covers.CoverItemLoader.TryGet(upstreamId, out int t)) return t;
		return Recipes.IngredientResolverImpl.Instance.ResolveItemType(upstreamId);
	}

	private Item MakeFilterStack(PipeFilterType type)
	{
		int t = ResolveFilterItemId(type);
		if (t <= 0) return new Item();
		var s = new Item();
		s.SetDefaults(t);
		s.stack = 1;
		return s;
	}

	// Passive: swap the cover instance. AttachItem dispatches filter type
	// (verbatim upstream ItemFilter.loadFilter(attachItem)).
	internal CoverBehavior? InstallFilterCover(CoverSide side, PipeFilterType type)
	{
		int i = (int)side;
		_filterCovers[i]?.OnUnload();

		if (type == PipeFilterType.None)
		{
			_filterCovers[i] = null;
			return null;
		}
		// Same cover def for Simple + Tag; AttachItem disambiguates.
		string registryId = Layer == PipeKind.Item ? "item_filter" : "fluid_filter";
		var def = CoverRegistry.Get(registryId);
		if (def is null) return null;
		var attachStack = MakeFilterStack(type);
		if (attachStack.IsAir) return null;

		var cover = def.CreateCoverBehavior(this, side);
		cover.OnAttached(attachStack);
		cover.OnLoad();
		_filterCovers[i] = cover;
		return cover;
	}

	// Active: cover stays in place; install-slot handler swaps filter item.
	// Empty stack -> EmptyFilter -> "no filtering".
	private void ApplyActiveFilterItem(CoverSide side, PipeFilterType type)
	{
		int i = (int)side;
		var cover = _robotArms[i];
		if (cover is null) return;
		if (Layer == PipeKind.Item)
		{
			cover.UiItemFilterHandler?.SetFilterItem(
				type == PipeFilterType.None ? new Item() : MakeFilterStack(type));
		}
		else
		{
			cover.UiFluidFilterHandler?.SetFilterItem(
				type == PipeFilterType.None ? new Item() : MakeFilterStack(type));
		}
	}

	// Pipe-side UI controls which covers reach the pipe; this is a safety belt.
	public bool CanPlaceCoverOnSide(CoverDefinition definition, CoverSide side) => true;

	void ICoverable.SaveCovers(TagCompound tag)
	{
		foreach (var side in CoverSides.All)
		{
			int i = (int)side;
			tag[$"mode_{i}"] = (byte)_modes[i];
			tag[$"ftype_{i}"] = (byte)_filterTypes[i];
			if (_filterCovers[i] is { } fc)
			{
				var sub = new TagCompound { ["id"] = fc.CoverDefinition.Id };
				fc.Save(sub);
				tag[$"filter_{i}"] = sub;
			}
			if (_robotArms[i] is { } ra)
			{
				var sub = new TagCompound { ["id"] = ra.CoverDefinition.Id };
				ra.Save(sub);
				tag[$"arm_{i}"] = sub;
			}
		}
	}

	void ICoverable.LoadCovers(TagCompound tag)
	{
		foreach (var side in CoverSides.All)
		{
			int i = (int)side;
			_modes[i] = tag.ContainsKey($"mode_{i}")
				? (PipeSideMode)tag.GetByte($"mode_{i}")
				: PipeSideMode.Off;
			_filterTypes[i] = tag.ContainsKey($"ftype_{i}")
				? (PipeFilterType)tag.GetByte($"ftype_{i}")
				: PipeFilterType.Simple;

			if (tag.ContainsKey($"filter_{i}"))
			{
				var sub = tag.GetCompound($"filter_{i}");
				var def = CoverRegistry.Get(sub.GetString("id"));
				if (def is not null && def.CreateCoverBehavior(this, side) is { } fc
					&& fc is (ItemFilterCover or FluidFilterCover))
				{
					_filterCovers[i]?.OnUnload();
					fc.Load(sub);
					fc.OnLoad();
					_filterCovers[i] = fc;
				}
			}
			else if (_filterCovers[i] is { } prevFilter)
			{
				// Same shape as ICoverable.LoadCovers: SaveCovers omits the
				// key for empty/cleared slots, so a sync after server-side
				// removal arrives with the key MISSING. Without this clear,
				// the client keeps rendering the stale filter cover.
				prevFilter.OnUnload();
				_filterCovers[i] = null;
			}
			if (tag.ContainsKey($"arm_{i}"))
			{
				var sub = tag.GetCompound($"arm_{i}");
				var def = CoverRegistry.Get(sub.GetString("id"));
				if (def is not null && def.CreateCoverBehavior(this, side) is { } ra
					&& ra is (RobotArmCover or FluidRegulatorCover))
				{
					_robotArms[i]?.OnUnload();
					ra.Load(sub);
					ra.OnLoad();
					_robotArms[i] = ra;
				}
			}
			else if (_robotArms[i] is { } prevArm)
			{
				prevArm.OnUnload();
				_robotArms[i] = null;
			}
		}
		NotifyBlockUpdate();
	}
}
