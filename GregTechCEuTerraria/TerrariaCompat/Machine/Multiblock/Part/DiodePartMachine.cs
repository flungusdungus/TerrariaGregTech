#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of DiodePartMachine. One-way energy valve - accepts on every side
// EXCEPT IoDirection (= "front"), emits only on IoDirection when
// WorkingEnabled. Soft-mallet cycles the amperage cap (1/2/4/8/16A).
public class DiodePartMachine : TieredIOPartMachine
{
	public const int MAX_AMPS = 16;

	public enum AmpMode
	{
		Mode1A  = 1,
		Mode2A  = 2,
		Mode4A  = 4,
		Mode8A  = 8,
		Mode16A = 16,
	}

	public static AmpMode GetByValue(int amps) => amps switch
	{
		2  => AmpMode.Mode2A,
		4  => AmpMode.Mode4A,
		8  => AmpMode.Mode8A,
		16 => AmpMode.Mode16A,
		_  => AmpMode.Mode1A,
	};

	protected override string Label => "Diode";

	public NotifiableEnergyContainer? EnergyContainer { get; protected set; }
	public int Amps { get; protected set; } = 1;

	public DiodePartMachine() : base() { }

	public void Configure(int tier)
	{
		Tier = tier;
		Io   = IO.BOTH;
		Amps = 1;
		EnsureEnergyContainer();
	}

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		if (Definition == null) return;
		Configure((int)((MetaMachine)this).Tier);
	}

	private void EnsureEnergyContainer()
	{
		if (EnergyContainer == null)
		{
			long tierVoltage = VoltageTiers.V(Tier);
			EnergyContainer = new NotifiableEnergyContainer(
				maxCapacity: tierVoltage * MAX_AMPS * 2,
				maxInputVoltage:  tierVoltage,
				maxInputAmperage: MAX_AMPS,
				maxOutputVoltage:  tierVoltage,
				maxOutputAmperage: MAX_AMPS);
			Traits.Attach(EnergyContainer);
			Traits.RegisterPersistent("EnergyContainer", EnergyContainer);
		}
		ReinitializeEnergyContainer();
	}

	protected void ReinitializeEnergyContainer()
	{
		if (EnergyContainer == null) return;
		long tierVoltage = VoltageTiers.V(Tier);
		EnergyContainer.ResetBasicInfo(tierVoltage * MAX_AMPS * 2, tierVoltage, Amps, tierVoltage, Amps);
		EnergyContainer.SideInputCondition  = s => s != IoDirection;
		EnergyContainer.SideOutputCondition = s => s == IoDirection && WorkingEnabled;
	}

	protected virtual int GetMaxAmperage() => MAX_AMPS;

	public void CycleAmpMode()
	{
		if (!IsServer) return;
		Amps = Amps >= GetMaxAmperage() ? 1 : Amps << 1;
		ReinitializeEnergyContainer();
		MachineStateSyncPacket.Broadcast(this);
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["amp_mode"] = Amps;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		Amps = tag.ContainsKey("amp_mode") ? tag.GetInt("amp_mode") : 1;
		EnsureEnergyContainer();
		Traits.Load(tag);   // late-registration re-load; ItemBus pattern.
	}
}
