#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Adapted port of com.gregtechceu.gtceu.common.machine.electric.MinerMachine.
// Upstream is a downward 3D box-scanner mining chunkSize-radius columns to
// bedrock into a (tier+1)^2 cache.
//
// DEVIATIONS (Terraria-adapted): no facing - flat WxD band below
// the machine (widths/depths in Width/Depth below); filter widened to
// TileID.Sets.Ore + gems, non-matching tiles skipped not broken; persistent
// _scanCursor sweeps the band; cache-full idles (upstream tryFillCache fail path).
//
// Energy: receiver, capacity V[tier]*64, per-tick draw V[tier-1] (LV pays ULV).
public sealed class MinerMachine : TieredEnergyMachine, IControllable
{
	public MinerMachine() { }
	public MinerMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "Miner";

	public override bool CanAccept => true;
	public override long EnergyCapacity => VoltageTiers.Voltage(Tier) * 64L;

	// V[tier-1] - upstream parity (LV pays ULV).
	private long EnergyPerTick
	{
		get
		{
			int idx = Math.Max(0, (int)Tier - 1);
			return VoltageTiers.Voltage((VoltageTier)idx);
		}
	}

	// Width: absolute tile count (EV jump is intentional end-game scale).
	// Depth: world-height fraction per tier (each hits a distinct layer from a
	// surface placement); falls back to 1200 before the world loads.
	public int Width => (int)Tier switch
	{
		1 => 32,
		2 => 48,
		3 => 64,
		4 => 128,
		_ => 16 * (int)Tier,
	};
	public int Depth
	{
		get
		{
			double frac = (int)Tier switch
			{
				1 => 0.35,
				2 => 0.55,
				3 => 0.75,
				4 => 0.95,
				_ => 0.35 + 0.20 * ((int)Tier - 1),
			};
			int h = Main.maxTilesY > 0 ? Main.maxTilesY : 1200;
			return (int)(h * frac);
		}
	}

	// Ticks/ore - tier-keyed constant (no uniform Terraria hardness scalar).
	private int TicksPerOre
	{
		get
		{
			int t = (int)Tier;
			return Math.Max(5, 45 - t * 8);
		}
	}

	protected override bool HasChargerSlot => true;

	// (tier+1)^2 output cache (CapabilityIO=OUT).
	private int InventorySize { get { int t = (int)Tier; return (t + 1) * (t + 1); } }

	private NotifiableItemStackHandler? _cache;
	private AutoOutputTrait? _autoOutput;
	private EnvironmentalExplosionTrait? _explosion;

	public NotifiableItemStackHandler Cache { get { EnsureTraits(); return _cache!; } }

	public override AutoOutputTrait? AutoOutput { get { EnsureTraits(); return _autoOutput; } }

	private void EnsureTraits()
	{
		if (_cache is not null) return;
		BindDefinition();
		EnsureEnergyContainer();

		_cache = new NotifiableItemStackHandler(InventorySize, IO.BOTH, IO.OUT);
		Traits.Attach(_cache);
		Traits.RegisterPersistent("Cache", _cache);

		// AutoOutputTrait.ofItems(cache) - handler-ref form.
		_autoOutput = AutoOutputTrait.OfItems(_cache);
		Traits.Attach(_autoOutput);
		Traits.RegisterPersistent("AutoOutput", _autoOutput);

		// Disable ambient-hazard explosions (project policy); over-voltage path stays armed.
		_explosion = Traits.GetTrait<EnvironmentalExplosionTrait>(EnvironmentalExplosionTrait.TYPE);
		_explosion?.SetEnableEnvironmentalExplosions(false);
	}

	protected override void OnMachineLoaded()
	{
		base.OnMachineLoaded();
		EnsureTraits();
	}

	// Cache -> InventoryOutput; charger handled by base TieredEnergyMachine.
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

	private int _progress;
	private int _targetX;
	private int _targetY;
	private bool _hasTarget;
	// Column to start the next FindTarget sweep from; walks the band L->R, wraps.
	private int _scanCursor;
	private int _lastDepth;

	protected override void OnTick()
	{
		EnsureTraits();

		bool canWork = _isWorkingEnabled && DrainEnergy(simulate: true);
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
			_progress = 0;
		}

		_active = true;
		DrainEnergy(simulate: false);

		_progress++;
		if (_progress >= TicksPerOre)
		{
			BreakTarget();
			_progress = 0;
			_hasTarget = false;
		}
	}

	// TileID.Sets.Ore covers metallic ores only; gems are a separate family.
	private static bool IsMineable(int type) =>
		TileID.Sets.Ore[type]
		|| type == TileID.Amethyst || type == TileID.Topaz || type == TileID.Sapphire
		|| type == TileID.Emerald || type == TileID.Ruby || type == TileID.Diamond
		|| type == TileID.ExposedGems;

	private bool TargetStillValid()
	{
		if (_targetX < 0 || _targetX >= Main.maxTilesX || _targetY < 0 || _targetY >= Main.maxTilesY)
			return false;
		var t = Main.tile[_targetX, _targetY];
		return t.HasTile && IsMineable(t.TileType);
	}

	// Scan the WxD band for the next ore; cursor advances one column per attempt,
	// vertical scan is shallow-first so the band peels layer-by-layer.
	private bool FindTarget(out int outX, out int outY)
	{
		int leftX  = Position.X + Size.Width / 2 - Width / 2;
		int rightX = leftX + Width - 1;
		int startY = Position.Y + Size.Height;
		int endY   = Math.Min(Main.maxTilesY - 1, startY + Depth - 1);

		// Try every column once starting from the cursor; advance the cursor
		// after each attempt so consecutive ticks distribute work across the band.
		for (int i = 0; i < Width; i++)
		{
			int x = leftX + ((_scanCursor + i) % Width);
			_scanCursor = (_scanCursor + 1) % Width;
			if (x < 0 || x >= Main.maxTilesX) continue;
			for (int y = startY; y <= endY; y++)
			{
				var t = Main.tile[x, y];
				if (!t.HasTile) continue;
				if (!IsMineable(t.TileType)) continue;
				outX = x; outY = y;
				return true;
			}
		}

		outX = outY = 0;
		return false;
	}

	private void BreakTarget()
	{
		if (_targetX < 0 || _targetX >= Main.maxTilesX || _targetY < 0 || _targetY >= Main.maxTilesY) return;
		var t = Main.tile[_targetX, _targetY];
		if (!t.HasTile || !IsMineable(t.TileType)) return;

		// Cache-first capture: snapshot active item slots, KillTile with
		// noItem=false so vanilla resolves the drop (extractinator gems, alt-evil
		// substitution, ModTile.Drop hooks), then post-sweep newly-active items
		// at the broken tile into the cache. tML patched KillTile_GetItemDrops
		// public->internal, so this post-sweep is the only universal drop path.
		var preActive = new bool[Main.maxItems];
		for (int i = 0; i < Main.maxItems; i++) preActive[i] = Main.item[i].active;

		WorldGen.KillTile(_targetX, _targetY, fail: false, effectOnly: false, noItem: false);
		if (Main.netMode == NetmodeID.Server && !Main.tile[_targetX, _targetY].HasTile)
			NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, _targetX, _targetY);

		// Newly-active items within a 2-tile box of the broken tile are ours.
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

	// Copy a dropped item into the cache + despawn it; overflow stays in-world
	// for the player to collect (no ore is ever destroyed).
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
			// Partial absorb - shrink the world stack to leftover.
			Main.item[itemIdx].stack = leftover.stack;
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendData(MessageID.SyncItem, -1, -1, null, itemIdx);
		}
	}

	// simulate-first drain
	private bool DrainEnergy(bool simulate)
	{
		long resultEnergy = EnergyContainer.EnergyStored - EnergyPerTick;
		if (resultEnergy >= 0L && resultEnergy <= EnergyContainer.EnergyCapacity)
		{
			if (!simulate) EnergyContainer.SetEnergyStored(resultEnergy);
			return true;
		}
		return false;
	}

	public override void SaveData(TagCompound tag)
	{
		EnsureTraits();
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
		EnsureTraits();
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
		lines.Add($"Draw: {EnergyPerTick:N0} EU/t");
		if (_active)
			lines.Add($"Mining at depth {_lastDepth} ({RecipeStatusText.FormatProgressSeconds(_progress, TicksPerOre)})");
		else if (!_isWorkingEnabled)
			lines.Add("Disabled");
		else
			lines.Add("Idle: no ore in range");
	}
}
