#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait.Multiblock;
using GregTechCEuTerraria.Api.Pattern;
using GregTechCEuTerraria.Api.Pattern.Error;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using GregTechCEuTerraria.TerrariaCompat.Net;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Casings;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Port of MultiblockControllerMachine. Owns the MultiblockState, bound
// IMultiPart list, IsFormed, and BlockPattern checker.
//
// Dropped: MachineRenderState (renderer reads IsFormed directly), facing/
// rotation hooks (2D), shift-RMB preview, MultiblockWorldSavedData async-check
// queue (gated Update() walk substitutes), getPartAppearance, @SyncToClient
// (rides MachineStateSync). Verbatim: OnStructureFormed/Invalid lifecycle,
// part list rebuild, CheckPattern lock (Monitor instead of ReentrantLock),
// AsyncCheckPattern offset formula.
public abstract class MultiblockControllerMachine : MetaMachine
{
	private MultiblockState? _multiblockState;
	private readonly List<IMultiPart> _parts = new();

	public Point16[] PartPositions { get; private set; } = Array.Empty<Point16>();

	public bool IsFormed { get; protected set; }

	// From Definition.FusedCasingTileName.
	private static readonly Dictionary<string, ushort> _fusedCasingCache = new();
	public virtual ushort FusedCasingTileType
	{
		get
		{
			var name = Definition?.FusedCasingTileName;
			if (string.IsNullOrEmpty(name)) return 0;
			if (_fusedCasingCache.TryGetValue(name, out var cached)) return cached;
			var mod = Terraria.ModLoader.ModLoader.GetMod("GregTechCEuTerraria");
			ushort id = mod.TryFind<Terraria.ModLoader.ModTile>(name, out var t) ? (ushort)t.Type : (ushort)0;
			_fusedCasingCache[name] = id;
			return id;
		}
	}

	// Resolved via TileLoader.GetTile by id - Mod.TryFind<CasingTile> can return
	// null even when TryFind<ModTile> resolves the name.
	public virtual string? FusedCasingTexture
	{
		get
		{
			var explicitPath = Definition?.FusedCasingTexturePath;
			if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;
			ushort type = FusedCasingTileType;
			if (type == 0) return null;
			return TileLoader.GetTile(type) is Tiles.Casings.CasingTile casing
				? casing.BlockTexture : null;
		}
	}

	// Flip pass unimplemented.
	public bool IsFlipped { get; protected set; }

	// Reentrant via Monitor.
	private readonly object _patternLock = new();

	// Snapshot of what we broadcast active so OnStructureInvalid withdraws the
	// EXACT same set even if the structure has since mutated.
	private bool _lastActive;
	private readonly List<Point16> _activeCells = new();

	private ParallelHatchPartMachine? _parallelHatch;
	public ParallelHatchPartMachine? GetParallelHatch() => _parallelHatch;

	// Per-position hash (deterministic - MP server/client consistency).
	private int _offset = -1;

	// MP clients can't run AsyncCheckPattern; PatternError isn't NBT-serializable,
	// so we capture resolved strings + the offending-cell coords + swap-hint
	// candidate item types here so the ghost-render hover still works on clients.
	private string?       _persistedUnformedReason;
	private List<string>? _persistedUnformedDetails;
	private int _persistedUnformedX = int.MinValue;
	private int _persistedUnformedY = int.MinValue;
	private int[]? _persistedSwapTypes;

	protected MultiblockControllerMachine() : base() { }

	// Fired on form transition or post-reload.
	public virtual void OnStructureFormed()
	{
		bool wasFormed = IsFormed;
		IsFormed = true;

		_parts.Clear();
		var set = GetMultiblockState().MatchContext
			.GetOrDefault("parts", (HashSet<IMultiPart>?)null) ?? new HashSet<IMultiPart>();
		foreach (var part in set)
		{
			if (ShouldAddPartToController(part))
				_parts.Add(part);
		}
		_parts.Sort(GetPartSorter());
		UpdatePartPositions();
		_parallelHatch = null;
		foreach (var part in _parts)
		{
			// First parallel hatch wins; PARALLEL_HATCH modifier reads off it.
			if (_parallelHatch == null && part is ParallelHatchPartMachine ph)
				_parallelHatch = ph;
			part.AddedToController(this);
		}
		UpdatePartPositions();
		foreach (var trait in Traits.AllTraits)
			if (trait is MultiblockMachineTrait mmt) mmt.OnStructureFormed();

		// Clients don't run AsyncCheckPattern - broadcast the edge.
		if (IsServer && !wasFormed)
			MultiblockFormedPacket.SendBroadcast(Position.X, Position.Y, true, IsFlipped);
	}

	// Controller break -> tear down BEFORE inventory drop so parts unbind +
	// active-casing withdraws this tick (otherwise visually stuck-on for a tick).
	public override void OnKill()
	{
		if (IsServer && IsFormed) OnStructureInvalid();
		base.OnKill();
	}

	public virtual void OnStructureInvalid()
	{
		bool wasFormed = IsFormed;
		IsFormed = false;
		_parallelHatch = null;

		foreach (var part in _parts)
			part.RemovedFromController(this);
		_parts.Clear();
		UpdatePartPositions();
		// formed->invalid drops here on mid-recipe casing break; no IsActive edge ever fires.
		if (_lastActive)
		{
			_lastActive = false;
			OnActiveStateChanged(false);
		}
		foreach (var trait in Traits.AllTraits)
			if (trait is MultiblockMachineTrait mmt) mmt.OnStructureInvalid();

		// Clients otherwise stay IsFormed until reload.
		if (IsServer && wasFormed)
			MultiblockFormedPacket.SendBroadcast(Position.X, Position.Y, false, IsFlipped);
	}

	// Bypasses pattern-walk side-effects - clients never have those.
	public void ApplyClientFormedSync(bool isFormed, bool isFlipped)
	{
		IsFormed = isFormed;
		IsFlipped = isFlipped;
		if (isFormed) GetMultiblockState().SetError(null);
	}

	public virtual void OnPartUnload()
	{
		_parts.RemoveAll(part => part.Self() is null);
		// Re-arm AsyncCheckPattern's gate on the next tick.
		GetMultiblockState().SetError(MultiblockState.UNLOAD_ERROR);
		UpdatePartPositions();
	}

	public MultiblockState GetMultiblockState()
	{
		if (_multiblockState is null)
			_multiblockState = new MultiblockState(Position.X, Position.Y);
		return _multiblockState;
	}

	// Live matcher state if available; falls back to persisted snapshot on MP
	// clients. Null while UNINIT (first few ticks after placement).
	public virtual string? GetUnformedReason()
	{
		var liveErr = GetMultiblockState().Error;
		if (liveErr is not null && liveErr != MultiblockState.UNINIT_ERROR)
			return MultiblockErrorText.Describe(liveErr);
		return _persistedUnformedReason;
	}

	public virtual IReadOnlyList<string> GetUnformedDetailLines()
	{
		var liveErr = GetMultiblockState().Error;
		if (liveErr is not null && liveErr != MultiblockState.UNINIT_ERROR)
			return new List<string>(MultiblockErrorText.DescribeLines(liveErr));
		return _persistedUnformedDetails ?? (IReadOnlyList<string>)System.Array.Empty<string>();
	}

	// Type 1/3 = not-enough global/per-layer. Live first, persisted fallback for MP.
	public virtual IReadOnlyList<int>? GetSwapCandidateTypes()
	{
		if (GetMultiblockState().Error is SinglePredicateError spe
			&& (spe.Type == 1 || spe.Type == 3))
		{
			var live = CandidateTypes(spe);
			if (live is { Length: > 0 }) return live;
		}
		return _persistedSwapTypes;
	}

	private static int[]? CandidateTypes(SinglePredicateError spe)
	{
		var cand = spe.Predicate.GetCandidates();
		if (cand.Count == 0) return null;
		var types = new int[cand.Count];
		for (int i = 0; i < cand.Count; i++) types[i] = cand[i].type;
		return types;
	}

	// Only the base PatternError carries a single cell ("Wrong block at X,Y").
	// SinglePredicateError + UNINIT/UNLOAD sentinels are structure-wide.
	public virtual (int X, int Y)? GetUnformedErrorCell()
	{
		var liveErr = GetMultiblockState().Error;
		if (liveErr is not null && liveErr.GetType() == typeof(PatternError))
			return (liveErr.GetX(), liveErr.GetY());
		if (_persistedUnformedX != int.MinValue)
			return (_persistedUnformedX, _persistedUnformedY);
		return null;
	}

	// For controllers that invalidate from within OnStructureFormed (matcher
	// said ok=true; persisted reason was just cleared). Survives save/load.
	protected void SetUnformedReason(string reason, System.Collections.Generic.IReadOnlyList<string>? details = null)
	{
		_persistedUnformedReason  = reason;
		_persistedUnformedDetails = details is null ? null : new List<string>(details);
	}

	// Upstream Comparator.comparingLong(part.self().getBlockPos().asLong()).
	public virtual Comparison<IMultiPart> GetPartSorter() =>
		(a, b) =>
		{
			var ap = a.Self().Position;
			var bp = b.Self().Position;
			long ak = ((long)(uint)(ushort)ap.Y << 32) | (uint)(ushort)ap.X;
			long bk = ((long)(uint)(ushort)bp.Y << 32) | (uint)(ushort)bp.X;
			return ak.CompareTo(bk);
		};

	// 2x2-per-cell anchor math (mirrors BlockPattern.CheckPatternAt).
	public bool TryGetPreviewCell(int tileX, int tileY, out char ch,
		out TraceabilityPredicate predicate)
	{
		ch = ' ';
		predicate = null!;
		if (IsFormed) return false;
		var pattern = GetPattern();
		if (pattern is null) return false;
		var preview = pattern.GetPreviewPattern();

		int originX = Position.X - preview.ControllerCol * 2;
		int originY = Position.Y - preview.ControllerRow * 2;
		int col = (tileX - originX) / 2;
		int row = (tileY - originY) / 2;
		if (row < 0 || row >= preview.Height) return false;
		if (col < 0 || col >= preview.Width)  return false;
		// Integer-divide bias on negatives.
		if (tileX < originX || tileY < originY) return false;

		ch = preview.Shape[row][col];
		if (!preview.Predicates.TryGetValue(ch, out predicate!)) return false;
		return true;
	}

	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		if (!IsFormed)
			AppendUnformedStructureBlock(lines);
	}

	// Header line + detail lines deduped. Callers must NOT also add
	// StatusLineForMulti - that embeds the reason and would duplicate it.
	protected void AppendUnformedStructureBlock(System.Collections.Generic.List<string> lines)
	{
		AppendUnformedStatusIfNeeded(lines);
		foreach (var detail in GetUnformedDetailLines())
			lines.Add($"[c/FF8888:{detail}]");
	}

	// Workable subclasses override to no-op (handled by their own AppendTooltip).
	protected virtual void AppendUnformedStatusIfNeeded(System.Collections.Generic.List<string> lines) =>
		lines.Add("[c/FFAA44:Structure not formed]");

	public IReadOnlyList<IMultiPart> GetParts()
	{
		// MP client / post-chunk-unload: rebuild from positions.
		if (_parts.Count != PartPositions.Length)
		{
			_parts.Clear();
			foreach (var pos in PartPositions)
			{
				if (MetaMachine.GetMachineAt(pos.X, pos.Y) is IMultiPart part)
					_parts.Add(part);
			}
		}
		return _parts;
	}

	public virtual bool IsBatchEnabled() => false;
	public virtual void SetBatchEnabled(bool batch) { }

	public void SetFlipped(bool flipped) => IsFlipped = flipped;

	protected void UpdatePartPositions()
	{
		PartPositions = _parts.Count == 0
			? Array.Empty<Point16>()
			: _parts.Select(part => part.Self().Position).ToArray();
	}

	public virtual bool ShouldAddPartToController(IMultiPart part) => true;

	public virtual bool AllowFlip() => Definition?.AllowFlip ?? false;

	public virtual bool AllowCircuitSlots() => true;

	public virtual IBlockPattern? GetPattern() => Definition?.PatternFactory?.Invoke();

	// Unsafe to call directly - matcher mutates state. Use Check*WithLock.
	public virtual bool CheckPattern()
	{
		var pattern = GetPattern();
		return pattern != null && pattern.CheckPatternAt(GetMultiblockState(), savePredicate: false);
	}

	public bool CheckPatternWithLock()
	{
		lock (_patternLock)
			return CheckPattern();
	}

	// Returns false on either failed check OR contended lock.
	public bool CheckPatternWithTryLock()
	{
		if (Monitor.TryEnter(_patternLock))
		{
			try { return CheckPattern(); }
			finally { Monitor.Exit(_patternLock); }
		}
		return false;
	}

	// No Forge neighborChanged equivalent: unformed re-check every 4 ticks,
	// formed every 20 (~1s) to catch player-broken casings.
	public void AsyncCheckPattern(long periodID)
	{
		if (_offset < 0)
			_offset = (int)(((Position.X * 13L + Position.Y * 7L) & 0x3FF) % 4);

		bool unformed = GetMultiblockState().HasError() || !IsFormed;
		int cadence = unformed ? 4 : 20;
		if ((_offset + periodID) % cadence != 0) return;

		bool ok = CheckPatternWithTryLock();
		// Clear stale "wrong block at (X,Y)" after a fix.
		if (ok)
		{
			_persistedUnformedReason  = null;
			_persistedUnformedDetails = null;
			_persistedUnformedX = int.MinValue;
			_persistedUnformedY = int.MinValue;
			_persistedSwapTypes = null;
		}
		else
		{
			var liveErr = GetMultiblockState().Error;
			if (liveErr is not null && liveErr != MultiblockState.UNINIT_ERROR)
			{
				_persistedUnformedReason  = MultiblockErrorText.Describe(liveErr);
				_persistedUnformedDetails = new List<string>(MultiblockErrorText.DescribeLines(liveErr));
				// Cell-specific only for the base PatternError.
				if (liveErr.GetType() == typeof(PatternError))
				{
					_persistedUnformedX = liveErr.GetX();
					_persistedUnformedY = liveErr.GetY();
				}
				else
				{
					_persistedUnformedX = int.MinValue;
					_persistedUnformedY = int.MinValue;
				}
				// Swap-hint candidates (type 1 global / 3 per-layer "not enough X").
				_persistedSwapTypes = liveErr is SinglePredicateError spe
					&& (spe.Type == 1 || spe.Type == 3)
					? CandidateTypes(spe)
					: null;
			}
		}

		if (ok)
		{
			// Verbatim asyncCheckPattern (java:353-369): unconditional
			// OnStructureFormed once the outer gate passes. Load-bearing for
			// the post-LoadData case (fresh entity has IsFormed=true from save
			// but _parts empty + UNINIT_ERROR; outer gate passes and aggregations
			// rebuild). Without it MP-restart broke recipe dispatch with
			// "No item input bus" forever.
			SetFlipped(GetMultiblockState().NeededFlip);
			OnStructureFormed();
		}
		else if (IsFormed)
		{
			OnStructureInvalid();
		}
	}

	protected override void OnTick()
	{
		base.OnTick();
		AsyncCheckPattern((long)Terraria.Main.GameUpdateCount);

		// Detect IsActive edges and notify the active-casing system.
		bool active = IsActive;
		if (active != _lastActive)
		{
			_lastActive = active;
			OnActiveStateChanged(active);
		}
	}

	// IsActive transition broadcast. On false we clear the LAST broadcast set
	// (not a re-walk) because the structure may already be invalid by then.
	protected virtual void OnActiveStateChanged(bool active)
	{
		if (!IsServer) return;

		if (active)
		{
			_activeCells.Clear();
			// EVERY cell, not just active-aware - lighting (MultiActiveLight)
			// reads ActiveCasingState across the whole footprint; face-swap
			// gates on IsActiveAware so non-aware tiles don't double-draw.
			foreach (var (cx, cy) in GetMultiblockState().GetCache())
				_activeCells.Add(new Point16(cx, cy));
			if (_activeCells.Count == 0) return;
			ActiveCasingState.SetActive(_activeCells);
			ActiveCasingPacket.SendBroadcast(Position.X, Position.Y, active: true, _activeCells);
		}
		else
		{
			if (_activeCells.Count == 0) return;
			ActiveCasingState.ClearActive(_activeCells);
			ActiveCasingPacket.SendBroadcast(Position.X, Position.Y, active: false, _activeCells);
			_activeCells.Clear();
		}
	}

	// Persist IsFormed + IsFlipped so a joining/chunk-loading client doesn't
	// have to wait for a formed-edge that already fired.
	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["mb_formed"]  = IsFormed;
		tag["mb_flipped"] = IsFlipped;
		// Snapshot the matcher's resolved error so MP clients (no pattern walker)
		// can show it in the GUI footer + world hover. Only emitted when set -
		// keeps the packet small for formed multis (the dominant case).
		if (_persistedUnformedReason is not null)
			tag["mb_unformed_reason"] = _persistedUnformedReason;
		if (_persistedUnformedDetails is { Count: > 0 })
			tag["mb_unformed_details"] = new List<string>(_persistedUnformedDetails);
		if (_persistedUnformedX != int.MinValue)
		{
			tag["mb_unformed_x"] = _persistedUnformedX;
			tag["mb_unformed_y"] = _persistedUnformedY;
		}
		if (_persistedSwapTypes is { Length: > 0 })
			tag["mb_swap_types"] = _persistedSwapTypes.ToList();
		// Sync the bound-part positions so MP clients can rebuild `_parts` via
		// GetParts() (the pattern walker that populates them is server-only -
		// SystemTick early-returns on IsClient). Without this every client-side
		// parts-walking display (energy aggregation, MultiblockInputDisplay's
		// "Current Inputs:" readout, ...) sees zero parts. Small + only present
		// when formed; MachineStateSync's dirty-skip drops it once stable.
		if (PartPositions.Length > 0)
		{
			var px = new int[PartPositions.Length];
			var py = new int[PartPositions.Length];
			for (int i = 0; i < PartPositions.Length; i++)
			{
				px[i] = PartPositions[i].X;
				py[i] = PartPositions[i].Y;
			}
			tag["mb_part_x"] = px;
			tag["mb_part_y"] = py;
		}
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("mb_formed"))  IsFormed  = tag.GetBool("mb_formed");
		if (tag.ContainsKey("mb_flipped")) IsFlipped = tag.GetBool("mb_flipped");
		_persistedUnformedReason  = tag.ContainsKey("mb_unformed_reason")  ? tag.GetString("mb_unformed_reason") : null;
		_persistedUnformedDetails = tag.ContainsKey("mb_unformed_details") ? tag.GetList<string>("mb_unformed_details").ToList() : null;
		_persistedUnformedX = tag.ContainsKey("mb_unformed_x") ? tag.GetInt("mb_unformed_x") : int.MinValue;
		_persistedUnformedY = tag.ContainsKey("mb_unformed_y") ? tag.GetInt("mb_unformed_y") : int.MinValue;
		_persistedSwapTypes = tag.ContainsKey("mb_swap_types") ? tag.GetList<int>("mb_swap_types").ToArray() : null;
		// Restore bound-part positions on MP clients (server re-walks + overwrites
		// these via OnStructureFormed). Clearing `_parts` forces GetParts() to
		// re-resolve the part entities from the synced positions on next read.
		if (tag.ContainsKey("mb_part_x") && tag.ContainsKey("mb_part_y"))
		{
			var px = tag.GetIntArray("mb_part_x");
			var py = tag.GetIntArray("mb_part_y");
			int n = System.Math.Min(px.Length, py.Length);
			var pos = new Point16[n];
			for (int i = 0; i < n; i++) pos[i] = new Point16(px[i], py[i]);
			PartPositions = pos;
			_parts.Clear();
		}
		// Deliberately DO NOT clear the multiblock state's UNINIT_ERROR here.
		// Upstream relies on a freshly-constructed MultiblockState carrying
		// UNINIT_ERROR to trip `asyncCheckPattern`'s `(hasError() || !isFormed)`
		// gate post-load, triggering the re-walk that repopulates `parts` and
		// `CapabilitiesProxy`. Suppressing the error would defeat that - the
		// gate would never pass for an IsFormed=true controller and the multi
		// would look formed but be unable to dispatch recipes ("No item input
		// bus" symptom). The error clears naturally inside CheckPatternWithLock
		// when the re-walk succeeds.
	}

	// True iff the (x, y) tile cell anchors an active-aware CasingTile - the
	// casing has a secondary face baked from the upstream blockstate's
	// `active=true` variant (or `_bloom` overlay).
	private static bool IsActiveAwareCasingAt(int x, int y)
	{
		if (x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY) return false;
		var tile = Main.tile[x, y];
		if (!tile.HasTile) return false;
		var modTile = TileLoader.GetTile(tile.TileType);
		return modTile is CasingTile c && c.IsActiveAware;
	}
}
