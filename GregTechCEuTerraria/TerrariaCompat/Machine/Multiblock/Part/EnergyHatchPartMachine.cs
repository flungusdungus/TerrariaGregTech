#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of EnergyHatchPartMachine. Emitter/receiver wrapping NEC; capacity
// 64xV[tier]xamp (emit) / 16xV[tier]xamp (recv).
public class EnergyHatchPartMachine : TieredIOPartMachine, IEnergyContainer
{
	protected override string Label => "Energy Hatch";

	public NotifiableEnergyContainer? EnergyContainer { get; protected set; }
	public int Amperage { get; protected set; }

	public long AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage) =>
		EnergyContainer?.AcceptEnergyFromNetwork(side, voltage, amperage) ?? 0;

	public bool InputsEnergy (IODirection side) => EnergyContainer?.InputsEnergy(side)  ?? false;
	public bool OutputsEnergy(IODirection side) => EnergyContainer?.OutputsEnergy(side) ?? false;

	public long ChangeEnergy(long differenceAmount) =>
		EnergyContainer?.ChangeEnergy(differenceAmount) ?? 0;

	public long EnergyStored   => EnergyContainer?.EnergyStored   ?? 0;
	public long EnergyCapacityRuntime => EnergyContainer?.EnergyCapacity ?? 0;
	long IEnergyContainer.EnergyCapacity => EnergyCapacityRuntime;

	// Compact per-field sync (was ~14 KB/s blob-resend before split).
	public override bool HasSyncEnergy => EnergyContainer != null;
	public override long SyncEnergyStored => EnergyContainer?.EnergyStored ?? 0;
	public override void ApplySyncEnergy(long energy) => EnergyContainer?.SetStoredFromSync(energy);

	public long InputAmperage  => EnergyContainer?.InputAmperage  ?? 0;
	public long InputVoltage   => EnergyContainer?.InputVoltage   ?? 0;
	long IEnergyContainer.OutputAmperage => EnergyContainer?.OutputAmperage ?? 0;
	long IEnergyContainer.OutputVoltage  => EnergyContainer?.OutputVoltage  ?? 0;

	// Face stays None - multi reaches via EnergyContainerList.
	public long GetPushAmperage() => EnergyContainer?.GetPushAmperage() ?? 0;
	public void OnEnergyPushedToNetwork(long amps, long voltage)
		=> EnergyContainer?.OnEnergyPushedToNetwork(amps, voltage);

	public EnergyHatchPartMachine() : base() { }

	public void Configure(int tier, IO io, int amperage)
	{
		Tier      = tier;
		Io        = io;
		Amperage  = amperage;
		// No SideInput/Output gates today - multi pulls via EnergyContainerList.
		EnsureEnergyContainer();
	}

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		var def = Definition;
		if (def?.PartIo == null || def.PartAmperage == 0) return;
		Configure((int)((MetaMachine)this).Tier, def.PartIo.Value, def.PartAmperage);
	}

	private void EnsureEnergyContainer()
	{
		if (EnergyContainer != null) return;
		long voltage = VoltageTiers.V(Tier);
		EnergyContainer = Io == IO.OUT
			? NotifiableEnergyContainer.EmitterContainer (GetHatchEnergyCapacity(Tier, Amperage), voltage, Amperage)
			: NotifiableEnergyContainer.ReceiverContainer(GetHatchEnergyCapacityReceiver(Tier, Amperage), voltage, Amperage);
		// No FrontFacing gate (same-cell sideless wires).
		Traits.Attach(EnergyContainer);
		Traits.RegisterPersistent("EnergyContainer", EnergyContainer);

		// Verbatim EnergyHatchPartMachine.java:33.
		if (Traits.GetTrait(Common.Machine.Trait.EnvironmentalExplosionTrait.TYPE) is null)
		{
			int tierIndex = (int)Tier;
			var capturedContainer = EnergyContainer;
			Traits.Attach(new Common.Machine.Trait.EnvironmentalExplosionTrait(
				explosionPower: tierIndex,
				fireChance:     tierIndex * 10,
				explosionPredicate: () => capturedContainer.EnergyStored > 0));
		}
	}

	public static long GetHatchEnergyCapacity(int tier, int amperage) =>
		VoltageTiers.V(tier) * 64L * amperage;

	public static long GetHatchEnergyCapacityReceiver(int tier, int amperage) =>
		VoltageTiers.V(tier) * 16L * amperage;

	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		long voltage = VoltageTiers.V(Tier);
		long capacity = Io == IO.OUT
			? GetHatchEnergyCapacity(Tier, Amperage)
			: GetHatchEnergyCapacityReceiver(Tier, Amperage);
		lines.Add($"Voltage: {voltage:N0} EU ({VoltageTiers.ShortName(((MetaMachine)this).Tier)})");
		lines.Add($"Amperage: {Amperage}");
		lines.Add($"Capacity: {capacity:N0} EU");
		if (EnergyContainer != null)
			lines.Add($"Stored: {EnergyContainer.EnergyStored:N0} / {EnergyContainer.EnergyCapacity:N0} EU");
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["amperage"] = Amperage;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		Amperage = tag.GetInt("amperage");
		EnsureEnergyContainer();
		Traits.Load(tag);   // late-registration re-load; ItemBus pattern.
	}
}
