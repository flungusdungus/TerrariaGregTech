#nullable enable
using System;
using GregTechCEuTerraria.Api.Fluids;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.api.cover.filter.FluidFilter.
public interface IFluidFilter : IFilter<FluidStack>
{
	// Configured amount for the supplied fluid; 0 if the fluid is not matched.
	int TestFluidAmount(FluidStack fluidStack);

	bool SupportsAmounts => !IsBlackList;

	// An empty fluid filter that allows all fluids. ONLY for matching.
	static readonly IFluidFilter Empty = new EmptyFluidFilter();

	private sealed class EmptyFluidFilter : IFluidFilter
	{
		public int TestFluidAmount(FluidStack fluidStack) => int.MaxValue;
		public bool Test(FluidStack fluidStack) => true;
		public TagCompound? SaveFilter() => null;
		public Action OnUpdated { get; set; } = () => { };
	}
}
