#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Machine.Trait;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

// Adapted port of SteamMinerMachine (+ SteamMinerLogic collapsed). Two variants
// lp/hp_steam_miner keyed off IsHighPressure. 3D box -> 2D WxD band; vent feature
// dropped; bespoke OnTick loop (no RecipeLogic) - same shape as electric Miner.
public sealed class SteamMinerMachine : SteamMachine, IControllable
{
	public SteamMinerMachine() : base() { }

	protected override string Label => Definition?.Label ?? (IsHighPressure ? "HP Steam Miner" : "Steam Miner");

	// Steam is INPUT here (filled by boiler/bucket); base ctors IO.OUT.
	protected override NotifiableFluidTank CreateSteamTank() =>
		new(1, SteamTankCapacity, Api.Capability.Recipe.IO.IN);

	// LP/HP parameters - SSOT for any rebalance.
	// Width: absolute tile count (machine-relative).
	// Depth: world-height-scaled. Steam miner stops at deep-Cavern; never
	// reaches Underworld even from worst-case surface placement (~0.21*H).
	//   LP 0.30  - mid-Cavern (water + early ores)
	//   HP 0.55  - deep-Cavern, ~100+ tiles short of Underworld top on every size
	public int Width          => IsHighPressure ? 24  : 16;
	public int Depth
	{
		get
		{
			double frac = IsHighPressure ? 0.55 : 0.30;
			int h = Main.maxTilesY > 0 ? Main.maxTilesY : 1200;
			return (int)(h * frac);
		}
	}

	// 2x upstream's `speed` (320/240) - the 2D band-scanner is less productive
	// per "useful" tick than upstream's 3D box, so the speed-up keeps the steam
	// miner in the right throughput ballpark for the steam age.
	public int TicksPerOre    => IsHighPressure ? 120 : 160;

	// DEVIATION: rate dropped 8x from upstream (16/32 -> 2/4 mB/t)
	// so per-ore cost lands on the same order as a steam-processing recipe.
	// LP->HP x2 ratio still matches upstream's LP=1.0/HP=2.0 mB/EU split.
	public int SteamPerTick   => IsHighPressure ? 4   : 2;

	// === Cache (4 slots, fixed) =============================================

	private const int InventorySize = 4;

	private NotifiableItemStackHandler? _cache;
	private AutoOutputTrait? _autoOutput;

	public NotifiableItemStackHandler Cache { get { EnsureMinerTraits(); return _cache!; } }

	public override AutoOutputTrait? AutoOutput { get { EnsureMinerTraits(); return _autoOutput; } }

	// SteamTank must attach first (fixed persistence key).
	private void EnsureMinerTraits()
	{
		if (_cache is not null) return;
		EnsureSteamTraits();

		_cache = new NotifiableItemStackHandler(InventorySize, IO.BOTH, IO.OUT);
		Traits.Attach(_cache);
		Traits.RegisterPersistent("Cache", _cache);

		// Handler-ref form = upstream `exportItems.exportToNearby(facing)`.
		_autoOutput = AutoOutputTrait.OfItems(_cache);
		Traits.Attach(_autoOutput);
		Traits.RegisterPersistent("AutoOutput", _autoOutput);
	}

	protected override void OnMachineLoaded()
	{
		base.OnMachineLoaded();
		EnsureMinerTraits();
	}

	public override Item[]? GetSlotGroup(SlotGroup group) => group switch
	{
		SlotGroup.InventoryOutput => Cache.Storage.Stacks,
		_                         => base.GetSlotGroup(group),
	};

	public override bool SupportsAutoOutputItems => true;

	private bool _isWorkingEnabled = true;
	public bool IsWorkingEnabled() => _isWorkingEnabled;
	public void SetWorkingEnabled(bool enabled) => _isWorkingEnabled = enabled;

	private bool _active;
	public override bool IsActive => _active;

	private int  _progress;
	private int  _targetX;
	private int  _targetY;
	private bool _hasTarget;
	private int  _scanCursor; // walks W-band left->right across ticks
	private int  _lastDepth;

	protected override void OnTick()
	{
		EnsureMinerTraits();

		bool canWork = _isWorkingEnabled && DrainSteam(simulate: true);
		if (!canWork)
		{
			if (_active || _progress != 0 || _hasTarget)
			{
				_active = false;
				_progress = 0;
				_hasTarget = false;
			}
			return;
		}

		if (!_hasTarget || !TargetStillValid())
		{
			if (!FindTarget(out _targetX, out _targetY))
			{
				_active = false;
				_progress = 0;
				_hasTarget = false;
				return;
			}
			_hasTarget = true;
			_progress  = 0;
		}

		_active = true;
		DrainSteam(simulate: false);

		_progress++;
		if (_progress >= TicksPerOre)
		{
			BreakTarget();
			_progress = 0;
			_hasTarget = false;
		}
	}

	private bool TargetStillValid()
	{
		if (_targetX < 0 || _targetX >= Main.maxTilesX || _targetY < 0 || _targetY >= Main.maxTilesY)
			return false;
		var t = Main.tile[_targetX, _targetY];
		return t.HasTile && TileID.Sets.Ore[t.TileType];
	}

	// Same band-scan as electric MinerMachine - see that file for cursor rationale.
	private bool FindTarget(out int outX, out int outY)
	{
		int leftX  = Position.X + Size.Width / 2 - Width / 2;
		int rightX = leftX + Width - 1;
		int startY = Position.Y + Size.Height;
		int endY   = Math.Min(Main.maxTilesY - 1, startY + Depth - 1);

		for (int i = 0; i < Width; i++)
		{
			int x = leftX + ((_scanCursor + i) % Width);
			_scanCursor = (_scanCursor + 1) % Width;
			if (x < 0 || x >= Main.maxTilesX) continue;
			for (int y = startY; y <= endY; y++)
			{
				var t = Main.tile[x, y];
				if (!t.HasTile) continue;
				if (!TileID.Sets.Ore[t.TileType]) continue;
				outX = x; outY = y;
				return true;
			}
		}

		outX = outY = 0;
		return false;
	}

	// Cache-first capture - see MinerMachine.BreakTarget for rationale.
	private void BreakTarget()
	{
		if (_targetX < 0 || _targetX >= Main.maxTilesX || _targetY < 0 || _targetY >= Main.maxTilesY) return;
		var t = Main.tile[_targetX, _targetY];
		if (!t.HasTile || !TileID.Sets.Ore[t.TileType]) return;

		var preActive = new bool[Main.maxItems];
		for (int i = 0; i < Main.maxItems; i++) preActive[i] = Main.item[i].active;

		WorldGen.KillTile(_targetX, _targetY, fail: false, effectOnly: false, noItem: false);
		if (Main.netMode == NetmodeID.Server && !Main.tile[_targetX, _targetY].HasTile)
			NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, _targetX, _targetY);

		float minX = (_targetX - 1) * 16f;
		float maxX = (_targetX + 2) * 16f;
		float minY = (_targetY - 1) * 16f;
		float maxY = (_targetY + 2) * 16f;
		for (int i = 0; i < Main.maxItems; i++)
		{
			if (preActive[i]) continue;
			ref var it = ref Main.item[i];
			if (!it.active || it.stack <= 0) continue;
			if (it.position.X < minX || it.position.X > maxX) continue;
			if (it.position.Y < minY || it.position.Y > maxY) continue;
			AbsorbDroppedItem(i);
		}

		_lastDepth = _targetY - (Position.Y + Size.Height) + 1;
	}

	private void AbsorbDroppedItem(int itemIdx)
	{
		var drop = Main.item[itemIdx];
		var storage = Cache.Storage;
		var leftover = drop;
		for (int i = 0; i < storage.SlotCount; i++)
		{
			leftover = storage.Insert(i, leftover, simulate: false);
			if (leftover.IsAir || leftover.stack <= 0) break;
		}
		Cache.OnContentsChanged();

		if (leftover.IsAir || leftover.stack <= 0)
		{
			Main.item[itemIdx].active = false;
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendData(MessageID.SyncItem, -1, -1, null, itemIdx);
		}
		else if (leftover.stack < drop.stack)
		{
			Main.item[itemIdx].stack = leftover.stack;
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendData(MessageID.SyncItem, -1, -1, null, itemIdx);
		}
	}

	// Verbatim port of upstream SteamMinerMachine.drainInput (vent gate not ported).
	private bool DrainSteam(bool simulate)
	{
		var stack = SteamTank.GetFluidInTank(0);
		long resultSteam = stack.Amount - SteamPerTick;
		if (resultSteam >= 0L && resultSteam <= SteamTank.GetTankCapacity(0))
		{
			if (!simulate) SteamTank.DrainInternal(SteamPerTick, simulate: false);
			return true;
		}
		return false;
	}

	public override void SaveData(TagCompound tag)
	{
		EnsureMinerTraits();
		base.SaveData(tag);
		tag["progress"]         = _progress;
		tag["active"]           = _active;
		tag["isWorkingEnabled"] = _isWorkingEnabled;
		tag["targetX"]          = _targetX;
		tag["targetY"]          = _targetY;
		tag["hasTarget"]        = _hasTarget;
		tag["scanCursor"]       = _scanCursor;
		tag["lastDepth"]        = _lastDepth;
	}

	public override void LoadData(TagCompound tag)
	{
		EnsureMinerTraits();
		base.LoadData(tag);
		_progress         = tag.GetInt("progress");
		_active           = tag.GetBool("active");
		_isWorkingEnabled = !tag.ContainsKey("isWorkingEnabled") || tag.GetBool("isWorkingEnabled");
		_targetX          = tag.GetInt("targetX");
		_targetY          = tag.GetInt("targetY");
		_hasTarget        = tag.GetBool("hasTarget");
		_scanCursor       = tag.GetInt("scanCursor");
		_lastDepth        = tag.GetInt("lastDepth");
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add($"Area: {Width} wide x {Depth} deep");
		lines.Add($"Speed: {TicksPerOre} ticks / ore");
		lines.Add($"Steam: {SteamPerTick} mB/t");
		if (_active)
			lines.Add($"Mining at depth {_lastDepth} ({RecipeStatusText.FormatProgressSeconds(_progress, TicksPerOre)})");
		else if (!_isWorkingEnabled)
			lines.Add("Disabled");
		else
		{
			var stack = SteamTank.GetFluidInTank(0);
			if (stack.Amount < SteamPerTick) lines.Add("Out of steam");
			else                              lines.Add("Idle: no ore in range");
		}
	}
}
