#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using RecipeIO = GregTechCEuTerraria.Api.Capability.Recipe.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

// 1:1 port of SteamLiquidBoilerMachine. Adds a fuelTank (IN) on top of
// SteamBoilerMachine; burns STEAM_BOILER recipes with fluid inputs (creosote +
// lava ship). FluidUtil filter replaced by RecipeRegistry walk.
public class SteamLiquidBoilerMachine : SteamBoilerMachine
{
	public SteamLiquidBoilerMachine() : base() { }

	protected override string Label => Definition?.Label ?? "Liquid Boiler";
	public override GTRecipeType GetRecipeType() => Definition?.RecipeType!;

	// Show fluid-fuel STEAM_BOILER recipes only (solid boiler runs item-fuel ones).
	public override bool ShowsInRecipeBrowser(GTRecipe recipe) =>
		recipe.GetInputContents(FluidRecipeCapability.CAP).Count > 0;

	// Upstream ConfigHolder liquidBoilerBaseOutput.
	protected override long GetBaseSteamOutput() => IsHighPressure ? 600 : 240;

	private NotifiableFluidTank? _fuelTank;
	public NotifiableFluidTank FuelTank { get { EnsureSteamTraits(); return _fuelTank!; } }

	protected virtual int FuelTankCapacity => 16_000;
	// Combined IFluidHandler indexing: water 0 / steam 1 / fuel 2.
	protected virtual int FuelTankAbsoluteIndex => 2;

	protected override void EnsureSteamTraits()
	{
		base.EnsureSteamTraits();
		if (_fuelTank is not null) return;
		_fuelTank = new NotifiableFluidTank(1, FuelTankCapacity, RecipeIO.IN);
		_fuelTank.SetFilter(IsFuelFluid);
		Traits.Attach(_fuelTank);
		Traits.RegisterPersistent("FuelTank", _fuelTank);
	}

	// RecipeRegistry walk by station id (= SteamSolidBoiler.IsFuelItem approach).
	private static readonly HashSet<string> _fuelFluidCache = new();
	private static bool _fuelCacheBuilt;

	private bool IsFuelFluid(FluidStack fluid)
	{
		if (fluid.IsEmpty || fluid.Type is null) return false;
		EnsureFuelCache();
		return _fuelFluidCache.Contains(fluid.Type.Id);
	}

	private void EnsureFuelCache()
	{
		if (_fuelCacheBuilt) return;
		_fuelCacheBuilt = true;
		foreach (var recipe in RecipeRegistry.ForStation(GetRecipeType().RegistryName))
			foreach (var c in recipe.GetInputContents(FluidRecipeCapability.CAP))
				if (c.Payload is FluidIngredient fi)
					foreach (var f in fi.GetFluids())
						_fuelFluidCache.Add(f.Id);
	}

	public override int TankCount => 3;

	public override FluidStack GetTank(int tank)
	{
		EnsureSteamTraits();
		if (tank == WaterTankAbsoluteIndex) return WaterTank.GetFluidInTank(0);
		if (tank == SteamTankAbsoluteIndex) return SteamTank.GetFluidInTank(0);
		return _fuelTank!.GetFluidInTank(0);
	}

	public override int GetCapacity(int tank) =>
		tank == FuelTankAbsoluteIndex ? FuelTankCapacity : base.GetCapacity(tank);

	public override bool IsFluidValid(int tank, FluidStack fluid)
	{
		if (fluid.IsEmpty) return false;
		if (tank == WaterTankAbsoluteIndex) return fluid.Type!.Id == FluidRegistry.Water.Id;
		if (tank == FuelTankAbsoluteIndex)  return IsFuelFluid(fluid);
		return false;  // steam = OUT
	}

	public override int Fill(FluidStack fluid, bool simulate)
	{
		if (fluid.IsEmpty) return 0;
		EnsureSteamTraits();
		if (fluid.Type!.Id == FluidRegistry.Water.Id) return WaterTank.FillInternal(fluid, simulate);
		if (IsFuelFluid(fluid))                       return _fuelTank!.FillInternal(fluid, simulate);
		return 0;
	}

	public override IFluidHandler GetTankAccess(int tank)
	{
		EnsureSteamTraits();
		if (tank == WaterTankAbsoluteIndex) return WaterTank.Storages[0];
		if (tank == SteamTankAbsoluteIndex) return SteamTank.Storages[0];
		return _fuelTank!.Storages[0];
	}

	// Fuel tank accepts both clicks (top up / scoop wrong fuel); water/steam use
	// boiler defaults. Mirrors upstream TankWidget pairs.
	public override (bool AllowFill, bool AllowDrain) GetTankClickCaps(int tank)
	{
		if (tank == FuelTankAbsoluteIndex) return (true, true);
		return base.GetTankClickCaps(tank);
	}

	// IN: water (local 0) + fuel (local 1). OUT: steam.
	public override int ResolveFluidTank(Api.Capability.Recipe.IO direction, int localIndex) =>
		direction == RecipeIO.IN
			? (localIndex == 0 ? WaterTankAbsoluteIndex : FuelTankAbsoluteIndex)
			: SteamTankAbsoluteIndex;
}
