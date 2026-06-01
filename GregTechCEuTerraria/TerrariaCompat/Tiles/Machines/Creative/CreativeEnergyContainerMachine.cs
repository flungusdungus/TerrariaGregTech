#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Creative;

// Port of com.gregtechceu.gtceu.common.machine.storage.CreativeEnergyContainerMachine.
// Infinite EU source/sink for debug - configurable voltage/amperage/direction +
// master active toggle. Implemented as MetaMachine + IEnergyContainer directly
// (no NEC trait, since configurable voltage/amps/source would mean re-creating
// the container on every change). EnergyStored=69 / EnergyCapacity=420 are
// upstream display sentinels (source mode returns long.MaxValue to dodge the
// wire-net's EnergyStored/OutputVoltage push clamp); OnEnergyPushedToNetwork
// records but never depletes.
//
// DEVIATIONS: ILaserContainer dropped (no laser pipes); over-voltage
// on the creative sink is a silent no-op (upstream level.explode - exploding a
// testing block isn't player intent); MAX tier ctor arg + setTier UI hint dropped
// (def pinned at MAX, voltage set directly via CreativeEnergyConfigAction).
public sealed class CreativeEnergyContainerMachine : MetaMachine, IEnergyContainer
{
	public CreativeEnergyContainerMachine() { }

	protected override string Label => Definition?.Label ?? "Creative Energy Container";

	// Verbatim upstream field set.
	private long _voltage      = 0;
	private int  _amps         = 1;
	private bool _active       = false;
	private bool _source       = true;     // true = output EU, false = accept EU
	private long _energyIOPerSec;          // rolling accumulator (every-tick add, every-20-tick snapshot)
	private long _lastAverageEnergyIOPerTick;
	private long _ampsReceived;            // per-tick counter, reset each tick

	public long Voltage
	{
		get => _voltage;
		set => _voltage = Math.Max(0, value);
	}
	public int Amps
	{
		get => _amps;
		set => _amps = Math.Max(0, value);
	}
	public bool Active
	{
		get => _active;
		set
		{
			if (_active == value) return;
			_active = value;
			// Wire-net classifies producers/consumers at build time off
			// OutputsEnergy/InputsEnergy (which read _active) - a runtime toggle
			// must re-link or it won't take effect until the net is dirtied.
			TerrariaCompat.Pipelike.Cable.EnergyNetSystem.MarkEndpointsDirty();
		}
	}
	public bool Source
	{
		get => _source;
		set
		{
			if (_source == value) return;
			_source = value;
			// Source/sink switch (java:214-225): source resets voltage/amps to 0
			// (player consciously dials a magnitude); sink flips to MAX (harmlessly
			// accepts any incoming voltage without overvoltage-explode).
			if (_source)
			{
				_voltage = 0;
				_amps    = 0;
			}
			else
			{
				_voltage = VoltageTiers.Voltage(VoltageTier.MAX);
				_amps    = int.MaxValue;
			}
			TerrariaCompat.Pipelike.Cable.EnergyNetSystem.MarkEndpointsDirty();
		}
	}
	public long LastAverageEnergyIOPerTick => _lastAverageEnergyIOPerTick;

	// Reset _ampsReceived + update the 20-tick rolling average. Producer push is
	// handled by the wire-net (OnEnergyPushedToNetwork records into _energyIOPerSec).
	protected override void OnTick()
	{
		// 20-tick snapshot. GetMcOffsetTimer (not GetOffsetTimer) - see its docs
		// for why GetOffsetTimer % FromMcTicks is unreachable on the 20 Hz path.
		if (GetMcOffsetTimer() % 20 == 0)
		{
			_lastAverageEnergyIOPerTick = _energyIOPerSec / 20;
			_energyIOPerSec = 0;
		}
		_ampsReceived = 0;
	}

	public long AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage)
	{
		if (_source || !_active || _ampsReceived >= _amps) return 0;
		if (voltage > _voltage)
		{
			// DEVIATION: upstream explodes on over-voltage; we no-op
			// (testing block). Route through EnvironmentalExplosionTrait.DoExplosionAt
			// if a real explode test is ever wanted.
			return Math.Min(amperage, _amps - _ampsReceived);
		}
		long accepted = Math.Min(amperage, _amps - _ampsReceived);
		if (accepted > 0)
		{
			_ampsReceived   += accepted;
			_energyIOPerSec += accepted * voltage;
			return accepted;
		}
		return 0;
	}

	public bool InputsEnergy(IODirection side) => !_source && _active;
	public bool OutputsEnergy(IODirection side) => _source && _active;

	public long ChangeEnergy(long differenceAmount)
	{
		// Only meaningful in sink mode; source-mode is a no-op.
		if (_source || !_active) return 0;
		_energyIOPerSec += differenceAmount;
		return differenceAmount;
	}

	public long EnergyStored
	{
		// Source mode returns long.MaxValue to dodge the wire-net's
		// EnergyStored/OutputVoltage push clamp; else upstream's sentinel 69.
		get => (_source && _active && _voltage > 0) ? long.MaxValue : 69;
	}
	public long EnergyCapacity => 420;

	// Gated on BOTH source/sink AND _active: the wire-net's overvoltage check
	// keys off OutputVoltage/InputVoltage > 0, so an inactive machine with a high
	// voltage configured would still burn cables / explode neighbours. _active
	// must zero them out.
	public long InputAmperage  => (!_source && _active) ? _amps    : 0;
	public long InputVoltage   => (!_source && _active) ? _voltage : 0;
	public long OutputVoltage  => ( _source && _active) ? _voltage : 0;
	public long OutputAmperage => ( _source && _active) ? _amps    : 0;

	// Record the pushed amount only - infinite source, never depletes (the
	// default impl would drain via ChangeEnergy).
	void IEnergyContainer.OnEnergyPushedToNetwork(long amps, long voltage)
	{
		_energyIOPerSec += amps * voltage;
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["voltage"] = _voltage;
		tag["amps"]    = _amps;
		tag["active"]  = _active;
		tag["source"]  = _source;
		tag["lastAvg"] = _lastAverageEnergyIOPerTick;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		_voltage = tag.GetLong("voltage");
		_amps    = tag.GetInt("amps");
		_active  = tag.GetBool("active");
		_source  = tag.ContainsKey("source") ? tag.GetBool("source") : true;
		_lastAverageEnergyIOPerTick = tag.GetLong("lastAvg");
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		if (_active)
		{
			string tier = _voltage <= 0
				? "(0)"
				: VoltageTiers.ShortName((VoltageTier)System.Math.Clamp(
					VoltageTiers.FloorTierByVoltage(_voltage), 0, (int)VoltageTier.MAX));
			lines.Add($"{(_source ? "Source" : "Sink")}: {tier} ({_voltage:N0} EU/t) x {_amps:N0} A");
		}
		else
		{
			lines.Add("Inactive");
		}
		lines.Add($"Avg I/O: {_lastAverageEnergyIOPerTick:N0} EU/t");
	}
}
