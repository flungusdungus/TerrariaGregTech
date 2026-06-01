#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Port of upstream TieredEnergyMachine. EU buffer + voltage/amperage live on
// a NotifiableEnergyContainer trait; IEnergyContainer surface forwards.
public abstract class TieredEnergyMachine : MetaMachine, IEnergyContainer
{
	protected TieredEnergyMachine() { }
	protected TieredEnergyMachine(VoltageTier tier) : base(tier) { }

	// Lazy attach - Tier resolves late; persistent register must precede base.LoadData.
	private NotifiableEnergyContainer? _energy;
	public NotifiableEnergyContainer EnergyContainer
	{
		get { EnsureEnergyContainer(); return _energy!; }
	}

	protected void EnsureEnergyContainer()
	{
		if (_energy is not null) return;
		BindDefinition();
		_energy = CreateEnergyContainer();
		Traits.Attach(_energy);
		Traits.RegisterPersistent("Energy", _energy);

		// Verbatim upstream: power = tier, fireChance = tierx10.
		if (Traits.GetTrait(EnvironmentalExplosionTrait.TYPE) is null)
		{
			int tierIndex = (int)Tier;
			var explosionTrait = new EnvironmentalExplosionTrait(
				explosionPower: tierIndex,
				fireChance:     tierIndex * 10,
				explosionPredicate: () => _energy is not null && _energy.EnergyStored > 0);
			Traits.Attach(explosionTrait);
		}
	}

	protected virtual NotifiableEnergyContainer CreateEnergyContainer()
	{
		long voltage = VoltageTiers.Voltage(Tier);
		if (CanAccept && CanExtract)
			return new NotifiableEnergyContainer(EnergyCapacity, voltage, 2, voltage, 1);
		if (CanExtract)
			return NotifiableEnergyContainer.EmitterContainer(EnergyCapacity, voltage, 1);
		return NotifiableEnergyContainer.ReceiverContainer(EnergyCapacity, voltage, 2);
	}

	public abstract long EnergyCapacity { get; }

	public virtual bool CanAccept  => false;
	public virtual bool CanExtract => false;

	public virtual long EnergyStored
	{
		get => EnergyContainer.EnergyStored;
		protected set => EnergyContainer.SetEnergyStored(value);
	}

	// Synced via MachineEnergySyncPacket. BatteryBuffer overrides to false
	// (derived from state-blob battery items).
	public override bool HasSyncEnergy => true;
	public override long SyncEnergyStored => EnergyContainer.EnergyStored;
	public override void ApplySyncEnergy(long energy) => EnergyContainer.SetStoredFromSync(energy);

	long IEnergyContainer.AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage)
		=> EnergyContainer.AcceptEnergyFromNetwork(side, voltage, amperage);

	public virtual bool InputsEnergy(IODirection side)  => EnergyContainer.InputsEnergy(side);
	public virtual bool OutputsEnergy(IODirection side) => EnergyContainer.OutputsEnergy(side);

	public virtual long ChangeEnergy(long differenceAmount) =>
		EnergyContainer.ChangeEnergy(differenceAmount);

	public virtual long InputAmperage  => EnergyContainer.InputAmperage;
	public virtual long InputVoltage   => EnergyContainer.InputVoltage;
	public virtual long OutputAmperage => EnergyContainer.OutputAmperage;
	public virtual long OutputVoltage  => EnergyContainer.OutputVoltage;

	// Forward so EnergyBatteryTrait overrides fire (DIM would bypass trait).
	public virtual long GetPushAmperage() => EnergyContainer.GetPushAmperage();
	public virtual void OnEnergyPushedToNetwork(long amps, long voltage) =>
		EnergyContainer.OnEnergyPushedToNetwork(amps, voltage);
	public virtual IODirection EnergyFaceForCell(int cellX, int cellY) => IODirection.None;

	// Legacy 2-arg surface still used by EnergyNet / EnergyRoutePath.
	public virtual long AcceptEnergy(long amount, VoltageTier sourceTier)
	{
		if (!CanAccept) return 0;
		long take = Math.Min(amount, EnergyCapacity - EnergyStored);
		if (take <= 0) return 0;
		ChangeEnergy(take);
		return take;
	}

	public virtual long ExtractEnergy(long maxAmount)
	{
		if (!CanExtract) return 0;
		long take = Math.Min(maxAmount, EnergyStored);
		if (take <= 0) return 0;
		ChangeEnergy(-take);
		return take;
	}

	public virtual long AcceptEnergyFromNetwork(long voltage, long amperage) =>
		EnergyContainer.AcceptEnergyFromNetwork(IODirection.Up, voltage, amperage);

	// Default off; opt-in via HasChargerSlot. Shared array for ItemSlot.LeftClick.
	private readonly Item[] _chargerInv = { new() };
	protected Item[] ChargerInv => _chargerInv;

	protected virtual bool HasChargerSlot => false;

	public override Item[]? GetSlotGroup(SlotGroup group) => group switch
	{
		SlotGroup.Charger => HasChargerSlot ? _chargerInv : null,
		_                 => base.GetSlotGroup(group),
	};

	internal override void SystemTick()
	{
		if (HasChargerSlot)
			EnergyContainer.DischargeOrRechargeEnergyContainers(_chargerInv, 0, simulate: false);
		base.SystemTick();
	}

	public override void SaveData(TagCompound tag)
	{
		EnsureEnergyContainer();
		base.SaveData(tag);
		if (HasChargerSlot) tag["ChargerSlot"] = ItemIO.Save(_chargerInv[0]);
	}

	public override void LoadData(TagCompound tag)
	{
		// Ensure before base.LoadData -> Traits.Load, else saved EU is dropped.
		EnsureEnergyContainer();
		base.LoadData(tag);
		if (HasChargerSlot && tag.ContainsKey("ChargerSlot"))
			_chargerInv[0] = ItemIO.Load(tag.GetCompound("ChargerSlot"));
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add($"Stored: {EnergyStored:N0} / {EnergyCapacity:N0} EU");

		// Skip "not connected" noise for hand-charged / adjacent-fed machines.
		var net = FindConnectedNetwork();
		if (net != null)
			lines.Add($"Network: {net.Cells.Count} cables * effective {VoltageTiers.ShortName(net.EffectiveTier)} {net.EffectiveAmperage}A (cap {net.PerTickCapacity:N0} EU/t, loss {net.MaxLossPerAmp}/A)");
	}

	// Same-cell wire model - query own footprint, not cardinal neighbours.
	private EnergyNet? FindConnectedNetwork()
	{
		foreach (var (cx, cy) in Cells())
		{
			var net = EnergyNetSystem.NetAt(cx, cy);
			if (net != null) return net;
		}
		return null;
	}
}
