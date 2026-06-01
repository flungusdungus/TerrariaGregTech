#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Primitive;

// Adapted port of PrimitivePumpMachine. Biome-keyed passive water generator;
// per 20t: biomeModifier x hatchModifier (x1.5 in rain). PumpBiomeModifier
// uses vanilla SceneMetrics.Scan. Underworld collapses to no-precipitation
// (only non-rain Terraria biome). hatchModifier=1 today (only pump_hatch 1B);
// 8B/larger cases unlock with multi-slot fluid hatches.
public class PrimitivePumpMachine : MultiblockControllerMachine
{
	protected override string Label => "Primitive Water Pump";

	private int _biomeModifier; // 0 = uncomputed, -1 = Underworld (no water), >0 mB/cycle
	private int _hatchModifier; // 0 = unbound; 1/2/4 from tank capacity
	private NotifiableFluidTank? _fluidTank;

	public PrimitivePumpMachine() : base() { }

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		InitializeTank();
	}

	public override void OnStructureInvalid()
	{
		base.OnStructureInvalid();
		ResetState();
	}

	public override void OnPartUnload()
	{
		base.OnPartUnload();
		ResetState();
	}

	private void ResetState()
	{
		_hatchModifier = 0;
		_fluidTank = null;
		// _biomeModifier preserved - biome doesn't change on hatch swap.
	}

	private void InitializeTank()
	{
		foreach (var part in GetParts())
		{
			if (part is not PumpHatchPartMachine hatch) continue;
			if (hatch.Tank is not { } tank) continue;
			int cap = tank.GetTankCapacity(0);
			_hatchModifier = cap switch
			{
				PumpBiomeModifier.BUCKET_VOLUME              => 1,
				PumpBiomeModifier.BUCKET_VOLUME * 8          => 2,
				_                                            => 4,
			};
			_fluidTank = tank;
			return;
		}
	}

	protected override void OnTick()
	{
		base.OnTick();
		if (!IsServer) return;
		if (!IsFormed) return;
		if (GetMultiblockState().HasError()) return;
		// MC-tick-aligned timer - GetOffsetTimer() % FromMcTicks(20) is
		// unreachable for ~2/3 of positions inside the 20 Hz OnTick gate (see
		// MetaMachine.GetMcOffsetTimer).
		if ((GetMcOffsetTimer() % 20) != 0) return;

		// Defer until first production tick - avoid scanning during structure
		// formation.
		if (_biomeModifier == 0)
		{
			_biomeModifier = PumpBiomeModifier.GetForTile(Position.X, Position.Y);
			return;
		}
		if (_biomeModifier < 0) return;
		if (_fluidTank == null) InitializeTank();
		if (_fluidTank == null) return;

		int amount = GetFluidProduction();
		if (amount <= 0) return;
		var water = new FluidStack(FluidRegistry.Water, amount);
		_fluidTank.Storages[0].Fill(water, simulate: false);
	}

	public int GetFluidProduction()
	{
		int value = _biomeModifier * _hatchModifier;
		if (IsRainingHere()) value = value * 3 / 2;
		return value;
	}

	// Underworld already short-circuits via biomeModifier == -1, so the
	// precipitation gate collapses to Main.raining.
	private static bool IsRainingHere() => Main.raining;

	// Must persist - layout polls these every frame; without persistence MP
	// clients show "Biome scan pending..." forever (state-sync round-trips Save).
	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["pp_biomeMod"] = _biomeModifier;
		tag["pp_hatchMod"] = _hatchModifier;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		_biomeModifier = tag.GetInt("pp_biomeMod");
		_hatchModifier = tag.GetInt("pp_hatchMod");
		// _fluidTank re-resolves on next form; _hatchModifier carries the readout.
	}
}
