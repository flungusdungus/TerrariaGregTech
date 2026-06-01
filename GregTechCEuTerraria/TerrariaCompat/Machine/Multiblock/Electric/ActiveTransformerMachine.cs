#nullable enable
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Multiblock;
using GregTechCEuTerraria.Api.Misc;
using GregTechCEuTerraria.Common.Machine.Trait;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Port of ActiveTransformerMachine. Power converter - bound input hatches feed
// bound output hatches each tick (no recipes). Structure invalid without both
// sides. Weakest-link routing emerges from EnergyContainerList.ChangeEnergy.
//
// ConditionalSubscriptionHandler collapsed (OnTick ticks every tick anyway).
// ioMap per-block override dropped (no caller). harmlessActiveTransformers
// config dropped - always explodes on structural failure while running (upstream
// default). GUI lives on AppendTooltip until a panel ships.
public class ActiveTransformerMachine : WorkableElectricMultiblockMachine
{
	private EnergyContainerList _powerInput  = new(new List<IEnergyContainer>());
	private EnergyContainerList _powerOutput = new(new List<IEnergyContainer>());

	// Display snapshot - the part walk is server-only, so MP clients receive
	// these via SaveData/MachineStateSyncPacket.
	private long _dispInputV, _dispInputA, _dispInputPerSec;
	private long _dispOutputV, _dispOutputA, _dispOutputPerSec;

	public ActiveTransformerMachine() : base() { }

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();

		// Verbatim upstream order: BOTH-direction handlers fall into INPUT first.
		var input  = new List<IEnergyContainer>();
		var output = new List<IEnergyContainer>();

		foreach (var part in GetPrioritySortedParts())
		{
			foreach (var rhl in part.GetRecipeHandlers())
			{
				// = upstream ioMap default (no per-block override).
				if (!rhl.IsValid(IO.BOTH)) continue;

				List<IEnergyContainer>? containers = null;
				foreach (var h in rhl.GetCapability(EURecipeCapability.CAP))
				{
					if (h is IEnergyContainer ec)
					{
						(containers ??= new List<IEnergyContainer>()).Add(ec);
					}
				}
				if (containers is null) continue;

				if (rhl.HandlerIO.Supports(IO.IN))
					input.AddRange(containers);
				else if (rhl.HandlerIO.Supports(IO.OUT))
					output.AddRange(containers);
			}
		}

		// Defensive - pattern factory already rejects hatchless structures.
		if (input.Count == 0 || output.Count == 0)
		{
			string which = input.Count == 0
				? (output.Count == 0 ? "no input AND no output hatches bound"
				                     : "no input energy hatches bound")
				: "no output energy hatches bound";
			SetUnformedReason(which, new[]
			{
				"Active Transformer needs at least one INPUT energy hatch + one OUTPUT energy hatch.",
				"(Substation Energy Hatches and Laser Hatches also satisfy these slots.)",
			});
			OnStructureInvalid();
			return;
		}

		_powerInput  = new EnergyContainerList(input);
		_powerOutput = new EnergyContainerList(output);
	}

	public override void OnStructureInvalid()
	{
		// Verbatim: explode at 6+tier if running when broken.
		if (Recipe.IsWorkingEnabled() && Recipe.IsWorking())
		{
			EnvironmentalExplosionTrait.DoExplosionAt(this, 6f + GetTier());
		}

		base.OnStructureInvalid();
		_powerInput  = new EnergyContainerList(new List<IEnergyContainer>());
		_powerOutput = new EnergyContainerList(new List<IEnergyContainer>());
		Recipe.SetStatus(RecipeLogicStatus.SUSPEND);
	}

	protected override void OnTick()
	{
		base.OnTick();
		if (!IsServer) return;
		if (IsFormed) ConvertEnergyTick();

		// Aligned to MachineStateSyncPacket's 6-tick BroadcastNearby cadence.
		if (IsFormed && (Terraria.Main.GameUpdateCount & 0x7) == 0)
			RefreshDisplaySnapshot();
	}

	private void RefreshDisplaySnapshot()
	{
		_dispInputV       = _powerInput.InputVoltage;
		_dispInputA       = _powerInput.InputAmperage;
		_dispInputPerSec  = _powerInput.GetInputPerSec();
		_dispOutputV      = _powerOutput.OutputVoltage;
		_dispOutputA      = _powerOutput.OutputAmperage;
		_dispOutputPerSec = _powerOutput.GetOutputPerSec();
	}

	// DEVIATION: upstream flips to SUSPEND on
	// input-dry / output-full, AND gates SetStatus on isWorkingEnabled() -
	// which flips false once SUSPEND lands, locking the multi until manual
	// power-toggle. We use IDLE for transient "nothing to transfer" instead
	// (doesn't lock; transitions back to WORKING when conditions allow).
	// SUSPEND reserved for the player's manual pause path.
	private void ConvertEnergyTick()
	{
		bool active = IsSubscriptionActive();

		if (!Recipe.IsSuspendAfterFinish())
			Recipe.SetStatus(active
				? RecipeLogicStatus.WORKING
				: RecipeLogicStatus.IDLE);

		if (active)
		{
			long canDrain    = _powerInput.EnergyStored;
			long totalDrained = _powerOutput.ChangeEnergy(canDrain);
			_powerInput.ChangeEnergy(-totalDrained);
		}
	}

	private bool IsSubscriptionActive()
	{
		if (!IsFormed) return false;
		if (_powerInput.EnergyStored <= 0) return false;
		if (_powerOutput.EnergyStored >= _powerOutput.EnergyCapacity) return false;
		return true;
	}

	// Cheap output hatches before substation/laser buffers.
	private List<IMultiPart> GetPrioritySortedParts()
	{
		var parts = GetParts().ToList();
		parts.Sort((a, b) => GetPartPriority(a).CompareTo(GetPartPriority(b)));
		return parts;
	}

	private static int GetPartPriority(IMultiPart part)
	{
		var mm = part.Self();
		ushort tileType = (ushort)Terraria.Main.tile[mm.Position.X, mm.Position.Y].TileType;
		if (PartAbility.OUTPUT_ENERGY            .IsApplicable(tileType)) return 1;
		if (PartAbility.SUBSTATION_OUTPUT_ENERGY .IsApplicable(tileType)) return 2;
		if (PartAbility.OUTPUT_LASER             .IsApplicable(tileType)) return 3;
		return 4;
	}

	public override int GetTier() => (int)Tier;

	// Skip WMM.AppendTooltip's "Running 0/0t" (DUMMY recipe). Inline DisplayName
	// + fancy info + unformed-reason; custom Paused/Running/Idle status.
	public override void AppendTooltip(List<string> lines)
	{
		lines.Add(DisplayName);
		OnAddFancyInformationTooltip(lines);

		if (!IsFormed)
		{
			AppendUnformedStructureBlock(lines);
			return;
		}

		if (!Recipe.IsWorkingEnabled())
			lines.Add("[c/FFFF55:Paused]");
		else if (Recipe.IsWorking())
			lines.Add("[c/55FF55:Running]");
		else
			lines.Add("Idle");

		// Per-sec sums are 20-tick rolling - divide for /tick.
		long maxIn  = System.Math.Abs(_dispInputV  * _dispInputA);
		long maxOut = System.Math.Abs(_dispOutputV * _dispOutputA);
		long avgIn  = System.Math.Abs(_dispInputPerSec  / 20);
		long avgOut = System.Math.Abs(_dispOutputPerSec / 20);
		lines.Add($"Max input: {maxIn:N0} EU/t");
		lines.Add($"Max output: {maxOut:N0} EU/t");
		lines.Add($"Avg in: {avgIn:N0} EU/t");
		lines.Add($"Avg out: {avgOut:N0} EU/t");
		// TEMP DBG (2026-05-29) - laser target -> dynamo investigation, transient.
		// Remove once root-cause confirmed (likely stale _handlerList cache).
		lines.Add($"[c/AAAAFF:DBG in: {_powerInput.EnergyStored:N0}/{_powerInput.EnergyCapacity:N0} EU]");
		lines.Add($"[c/AAAAFF:DBG out: {_powerOutput.EnergyStored:N0}/{_powerOutput.EnergyCapacity:N0} EU]");
		lines.Add($"[c/AAAAFF:DBG status: {Recipe.GetStatus()} subActive: {IsSubscriptionActive()}]");
		lines.Add("[c/FF5555:Warning: breaking while running will explode]");
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["at_inV"]    = _dispInputV;
		tag["at_inA"]    = _dispInputA;
		tag["at_inPS"]   = _dispInputPerSec;
		tag["at_outV"]   = _dispOutputV;
		tag["at_outA"]   = _dispOutputA;
		tag["at_outPS"]  = _dispOutputPerSec;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("at_inV"))   _dispInputV       = tag.GetLong("at_inV");
		if (tag.ContainsKey("at_inA"))   _dispInputA       = tag.GetLong("at_inA");
		if (tag.ContainsKey("at_inPS"))  _dispInputPerSec  = tag.GetLong("at_inPS");
		if (tag.ContainsKey("at_outV"))  _dispOutputV      = tag.GetLong("at_outV");
		if (tag.ContainsKey("at_outA"))  _dispOutputA      = tag.GetLong("at_outA");
		if (tag.ContainsKey("at_outPS")) _dispOutputPerSec = tag.GetLong("at_outPS");
	}
}
