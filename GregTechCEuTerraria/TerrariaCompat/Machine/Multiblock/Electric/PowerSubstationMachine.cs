#nullable enable
using System.Collections.Generic;
using System.Numerics;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Multiblock;
using GregTechCEuTerraria.Api.Misc;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Port of PowerSubstationMachine. Bulk EU storage multi. OnStructureFormed
// aggregates input/output hatch EnergyContainerLists + scans footprint for
// battery blocks (PssBatteryData) into a PowerStationEnergyBank (BigInteger
// total). Every 20 ticks: input hatches -> bank -> output hatches, passive
// drain off the top.
//
// Adaptations: maintenance multiplier no-op (subsystem not ported); ioMap
// per-block override dropped (parts default to declared Io via trait walk);
// battery scan walks the formed footprint instead of upstream's match-context
// PMC_BATTERY_HEADER buckets.
public class PowerSubstationMachine : WorkableMultiblockMachine
{
	public const int  MaxBatteryLayers = 18;
	public const int  MinCasings        = 14;
	// 1 % capacity per 24 hours (20 Hz).
	public const long PassiveDrainDivisor = 20L * 60 * 60 * 24 * 100;
	public const long PassiveDrainMaxPerStorage = 100_000L;

	private PowerStationEnergyBank? _energyBank;
	private EnergyContainerList?    _inputHatches;
	private EnergyContainerList?    _outputHatches;
	private long _passiveDrain;

	// 20-tick rolling stats - upstream inputPerSec/outputPerSec.
	private long _netInLastSec;
	private long _netOutLastSec;
	private long _inputPerSec;
	private long _outputPerSec;

	protected override string Label => Definition?.Label ?? "Power Substation";

	public PowerSubstationMachine() : base() { }

	public PowerStationEnergyBank EnergyBank => _energyBank ??= EnsureEnergyBank();

	private PowerStationEnergyBank EnsureEnergyBank()
	{
		BindDefinition();
		var bank = new PowerStationEnergyBank(System.Array.Empty<IBatteryData>());
		Traits.Attach(bank);
		Traits.RegisterPersistent("substation_bank", bank);
		return bank;
	}

	protected override void OnTick()
	{
		_energyBank ??= EnsureEnergyBank();
		base.OnTick();
		if (!IsServer || !IsFormed) return;
		TransferEnergyTick();
	}

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		_energyBank ??= EnsureEnergyBank();

		// Aggregate hatches (verbatim ActiveTransformer.OnStructureFormed shape,
		// upstream lines 93-126). BOTH falls into INPUT first.
		var inputs  = new List<IEnergyContainer>();
		var outputs = new List<IEnergyContainer>();
		foreach (var part in GetParts())
		{
			foreach (var rhl in part.GetRecipeHandlers())
			{
				if (!rhl.IsValid(IO.BOTH)) continue;
				List<IEnergyContainer>? containers = null;
				foreach (var h in rhl.GetCapability(EURecipeCapability.CAP))
					if (h is IEnergyContainer ec)
						(containers ??= new List<IEnergyContainer>()).Add(ec);
				if (containers is null) continue;
				if (rhl.HandlerIO.Supports(IO.IN))       inputs.AddRange(containers);
				else if (rhl.HandlerIO.Supports(IO.OUT)) outputs.AddRange(containers);
			}
		}
		_inputHatches  = new EnergyContainerList(inputs);
		_outputHatches = new EnergyContainerList(outputs);

		var batteries = new List<IBatteryData>();
		foreach (var (x, y) in EnumerateFootprintCells())
		{
			var tile = Terraria.Main.tile[x, y];
			if (!tile.HasTile) continue;
			if (Terraria.ModLoader.TileLoader.GetTile(tile.TileType) is not
			    Tiles.Casings.CasingTile casingTile) continue;
			var data = PssBatteryData.Get(casingTile.Name);
			if (data is null) continue;
			if (data.Tier == -1 || data.Capacity <= 0) continue;  // empty filler
			batteries.Add(data);
		}

		// Upstream verbatim (line 136). Persist a reason since the cell-level
		// matcher accepts empty filler variants.
		if (batteries.Count == 0)
		{
			SetUnformedReason(
				"No filled battery blocks installed",
				new[]
				{
					"At least one of the inner B cells must hold a real battery block:",
					"  EV / IV / LuV / ZPM / UV Lapotronic battery, or UHV Ultimate Battery.",
					"Empty Tier I / II / III blocks are filler only and don't contribute capacity.",
				});
			OnStructureInvalid();
			return;
		}

		_energyBank.Rebuild(batteries);
		_passiveDrain = _energyBank.GetPassiveDrainPerTick();
	}

	public override void OnStructureInvalid()
	{
		// Keep _energyBank - stored energy must survive rebuilds (upstream 147-149).
		_inputHatches  = null;
		_outputHatches = null;
		_passiveDrain  = 0;
		_netInLastSec  = 0;
		_inputPerSec   = 0;
		_netOutLastSec = 0;
		_outputPerSec  = 0;
		base.OnStructureInvalid();
	}

	// Walks every cell the last match visited (MultiblockState.Cache), not just
	// IMultiPart instances - batteries are CasingTiles and would be missed by
	// PartPositions.
	private IEnumerable<(int X, int Y)> EnumerateFootprintCells() =>
		GetMultiblockState().GetCache();

	// Verbatim transferEnergyTick (lines 160-189).
	private void TransferEnergyTick()
	{
		// Stats rollover every 20 MC ticks (= 1 sec at SimSpeed=1.0).
		if (Terraria.Main.GameUpdateCount % (uint)global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) == 0)
		{
			// Status flip drives the active overlay only - real work below.
			Recipe.SetStatus(_energyBank!.HasEnergy()
				? Api.Machine.Feature.RecipeLogicStatus.WORKING
				: Api.Machine.Feature.RecipeLogicStatus.IDLE);
			_inputPerSec  = _netInLastSec;
			_outputPerSec = _netOutLastSec;
			_netInLastSec  = 0;
			_netOutLastSec = 0;
		}

		if (!Recipe.IsWorkingEnabled() || _inputHatches is null || _outputHatches is null) return;

		long banked = _energyBank!.Fill(_inputHatches.EnergyStored);
		_inputHatches.ChangeEnergy(-banked);
		_netInLastSec += banked;

		long passiveDrained = _energyBank.Drain(GetPassiveDrain());
		_netOutLastSec += passiveDrained;

		long debanked = _energyBank.Drain(_outputHatches.EnergyCapacity - _outputHatches.EnergyStored);
		_outputHatches.ChangeEnergy(debanked);
		_netOutLastSec += debanked;
	}

	// Maintenance multiplier no-op (subsystem not ported).
	public long GetPassiveDrain() => _passiveDrain;

	// Custom tooltip skips WMM's recipe-shaped status line (would print
	// "Running 0/0t" for a DUMMY recipe). Inline MetaMachine.AppendTooltip
	// + the unformed-reason path manually. Same shape as ActiveTransformer.
	public override void AppendTooltip(List<string> lines)
	{
		lines.Add(DisplayName);
		OnAddFancyInformationTooltip(lines);

		if (!IsFormed)
		{
			AppendUnformedStructureBlock(lines);
			return;
		}

		if (_energyBank is null) return;
		AppendLiveStats(lines);
	}

	// Port of upstream addDisplayText formed-state block (lines 194-259).
	// Shared by world hover + PowerSubstationLayout display panel. Three-state
	// Paused/Running/Idle (no progress - DUMMY recipe); duration in seconds.
	private void AppendLiveStats(List<string> lines)
	{
		var bank = _energyBank!;

		if (!Recipe.IsWorkingEnabled())
			lines.Add("[c/FFFF55:Paused]");
		else if (bank.HasEnergy())
			lines.Add("[c/55FF55:Running]");
		else
			lines.Add("Idle");

		BigInteger stored   = bank.GetStored();
		BigInteger capacity = bank.Capacity;
		lines.Add($"Stored: {stored:N0} / {capacity:N0} EU");
		lines.Add($"Passive drain: [c/FF8888:{GetPassiveDrain():N0} EU/t]");
		lines.Add($"Avg in: [c/55FF55:{_inputPerSec / 20:N0} EU/t]");
		lines.Add($"Avg out: [c/FF8888:{System.Math.Abs(_outputPerSec) / 20:N0} EU/t]");

		// Time-to-fill/drain in seconds (upstream uses translatable keys).
		long net = _inputPerSec - _outputPerSec;
		if (net > 0)
		{
			BigInteger seconds = (capacity - stored) / new BigInteger(net);
			lines.Add($"Time to full: [c/55FF55:~{FormatDuration(seconds)}]");
		}
		else if (net < 0)
		{
			BigInteger seconds = stored / new BigInteger(-net);
			lines.Add($"Time to empty: [c/FF8888:~{FormatDuration(seconds)}]");
		}
	}

	// Port of upstream createUIWidget's ComponentPanelWidget(addDisplayText).
	// PowerSubstation extends WMM (not WEMM), so can't reuse generic_multi's
	// layout (its getter casts to WEMM).
	public IReadOnlyList<string> BuildPanelLines()
	{
		var lines = new List<string>();
		if (!IsFormed)
		{
			lines.Add(RecipeStatusText.StatusLineForMulti(this, Recipe));
			return lines;
		}
		if (_energyBank is not null) AppendLiveStats(lines);
		return lines;
	}

	private static string FormatDuration(BigInteger seconds)
	{
		if (seconds > new BigInteger(long.MaxValue)) return "forever";
		long s = (long)seconds;
		if (s <= 180) return $"{s} s";
		if (s <= 180 * 60) return $"{s / 60} min";
		if (s <= 72 * 3600) return $"{s / 3600} h";
		if (s <= 730L * 86400) return $"{s / 86400} d";
		return $"{s / (86400L * 365)} yr";
	}

	// Bank rides Traits.Save; avg stats need explicit save for MP visibility.
	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["pss_inPerSec"]   = _inputPerSec;
		tag["pss_outPerSec"]  = _outputPerSec;
		tag["pss_passive"]    = _passiveDrain;
	}

	public override void LoadData(TagCompound tag)
	{
		// Ensure bank BEFORE base.LoadData runs Traits.Load - else the bank's
		// persisted long[] storage is skipped and stored EU is lost on reload.
		_energyBank ??= EnsureEnergyBank();
		base.LoadData(tag);
		if (tag.ContainsKey("pss_inPerSec"))  _inputPerSec  = tag.GetLong("pss_inPerSec");
		if (tag.ContainsKey("pss_outPerSec")) _outputPerSec = tag.GetLong("pss_outPerSec");
		if (tag.ContainsKey("pss_passive"))   _passiveDrain = tag.GetLong("pss_passive");
	}
}
