#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Adapted port of com.gregtechceu.gtceu.common.machine.electric.BlockBreakerMachine.
// Upstream is a front-facing single-tile drill feeding a (tier+1)^2 cache.
//
// DEVIATIONS (Terraria-adapted): no facing - vertical-column drill
// (scans rows below for the shallowest breakable tile); world-height-fraction
// Range per tier (see Range); no cache - drops fall in-world; per-tile time is
// a tier-keyed constant (no uniform Terraria hardness scalar).
//
// Energy: receiver, capacity V[tier]*64, per-tick draw V[tier-1] (LV pays ULV).
public sealed class BlockBreakerMachine : TieredEnergyMachine, IControllable
{
	public BlockBreakerMachine() { }
	public BlockBreakerMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "Block Breaker";

	public override bool CanAccept => true;
	public override long EnergyCapacity => VoltageTiers.Voltage(Tier) * 64L;

	// V[tier-1] (java:82)
	private long EnergyPerTick
	{
		get
		{
			int idx = Math.Max(0, (int)Tier - 1);
			return VoltageTiers.Voltage((VoltageTier)idx);
		}
	}

	// World-height-fraction vertical reach per tier (each opens new territory
	// from a surface placement); falls back to 1200 before the world loads.
	public int Range
	{
		get
		{
			double frac = (int)Tier switch
			{
				1 => 0.20,
				2 => 0.35,
				3 => 0.50,
				4 => 0.65,
				5 => 0.80,
				6 => 0.95,
				7 => 1.00,
				_ => 0.20 + 0.15 * ((int)Tier - 1),
			};
			int h = Main.maxTilesY > 0 ? Main.maxTilesY : 1200;
			return (int)(h * frac);
		}
	}

	// Ticks/tile - tier-keyed constant (no uniform Terraria hardness scalar).
	private int TicksPerTile
	{
		get
		{
			int t = (int)Tier;
			return Math.Max(3, 45 - t * 8);
		}
	}

	protected override bool HasChargerSlot => true;

	// Presence is the idempotency gate (trait-reference, not bool flag).
	private EnvironmentalExplosionTrait? _explosion;
	private void EnsureTraits()
	{
		if (_explosion is not null) return;
		BindDefinition();
		EnsureEnergyContainer();   // attaches EnvironmentalExplosionTrait
		// Mirror upstream setEnableEnvironmentalExplosions(false).
		_explosion = Traits.GetTrait<EnvironmentalExplosionTrait>(EnvironmentalExplosionTrait.TYPE);
		_explosion?.SetEnableEnvironmentalExplosions(false);
	}

	protected override void OnMachineLoaded()
	{
		base.OnMachineLoaded();
		EnsureTraits();
	}

	private bool _isWorkingEnabled = true;
	public bool IsWorkingEnabled() => _isWorkingEnabled;
	public void SetWorkingEnabled(bool enabled) => _isWorkingEnabled = enabled;

	private bool _active;
	public override bool IsActive => _active;

	private int _progress;
	private int _targetX;
	private int _targetY;
	private bool _hasTarget;
	private int _lastDepth;   // cosmetic - surfaced in tooltip

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

		// Re-pick if no target or the current one vanished (e.g. mined by another player).
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
		if (_progress >= TicksPerTile)
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
		return t.HasTile && !IsProtected(t.TileType);
	}

	// Scan each footprint column downward for the shallowest breakable tile
	// within Range. Protected tiles are skipped (not blocking) so clutter or a
	// chest in the way doesn't jam the drill.
	private bool FindTarget(out int outX, out int outY)
	{
		int startY = Position.Y + Size.Height;       // row just below bottom edge
		int endY   = Math.Min(Main.maxTilesY - 1, startY + Range - 1);
		int bestY  = int.MaxValue;
		int bestX  = -1;

		for (int dx = 0; dx < Size.Width; dx++)
		{
			int x = Position.X + dx;
			if (x < 0 || x >= Main.maxTilesX) continue;
			for (int y = startY; y <= endY; y++)
			{
				var t = Main.tile[x, y];
				if (!t.HasTile) continue;
				if (IsProtected(t.TileType)) continue; // skip but keep scanning the column
				if (y < bestY) { bestY = y; bestX = x; }
				break; // shallowest in this column wins
			}
		}

		if (bestX < 0) { outX = outY = 0; return false; }
		outX = bestX; outY = bestY;
		return true;
	}

	// Tiles the drill refuses to touch: progression-gated bricks (dungeon /
	// lihzahrd / shimmer) and containers (covers our CrateTile via IsAContainer).
	// A type-level tile-entity test was considered but is too broad (modded
	// furniture) - promote to a tier-progression table later if needed.
	private static bool IsProtected(ushort tileType)
	{
		if (Main.tileDungeon[tileType]) return true;
		if (tileType == TileID.LihzahrdBrick) return true;
		if (tileType == TileID.ShimmerBlock) return true;
		if (TileID.Sets.IsAContainer[tileType]) return true;
		return false;
	}

	private void BreakTarget()
	{
		if (_targetX < 0 || _targetX >= Main.maxTilesX || _targetY < 0 || _targetY >= Main.maxTilesY) return;
		var t = Main.tile[_targetX, _targetY];
		if (!t.HasTile) return;

		// KillTile handles drops + FX + neighbour update; server broadcasts
		// TileManipulation. Server-only here.
		WorldGen.KillTile(_targetX, _targetY, fail: false, effectOnly: false, noItem: false);
		if (Main.netMode == NetmodeID.Server && !Main.tile[_targetX, _targetY].HasTile)
			NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, _targetX, _targetY);

		_lastDepth = _targetY - (Position.Y + Size.Height) + 1;
	}

	// simulate-first drain (java:238)
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
		_lastDepth        = tag.GetInt("lastDepth");
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add($"Range: {Range} tiles below");
		lines.Add($"Speed: {TicksPerTile} ticks / tile");
		lines.Add($"Draw: {EnergyPerTick:N0} EU/t");
		if (_active)
			lines.Add($"Drilling at depth {_lastDepth} ({RecipeStatusText.FormatProgressSeconds(_progress, TicksPerTile)})");
		else if (!_isWorkingEnabled)
			lines.Add("Disabled");
		else
			lines.Add("Idle: nothing to drill");
	}
}
