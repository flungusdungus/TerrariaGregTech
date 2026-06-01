#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of ParallelHatchPartMachine. Holds an integer "parallel count" for its
// controller. Max scales by tier: 4 ^ (tier - EV) -> EV=1, IV=4, LuV=16, ...
public class ParallelHatchPartMachine : TieredPartMachine
{
	public const int MIN_PARALLEL = 1;

	protected override string Label => "Parallel Control Hatch";

	public int MaxParallel     { get; private set; } = 1;
	public int CurrentParallel { get; private set; } = 1;

	// Always-on overlay animation - the hatch has no recipe logic; without
	// this the cosmetic pulse only plays while the bound multi is running.
	public override bool IsActive => true;

	public ParallelHatchPartMachine() : base() { }

	public void Configure(int tier)
	{
		Tier            = tier;
		MaxParallel     = (int)Math.Pow(4, tier - (int)VoltageTier.EV);
		CurrentParallel = MaxParallel;
	}

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		if (Definition == null) return;
		Configure((int)((MetaMachine)this).Tier);
	}

	public void SetCurrentParallel(int parallelAmount)
	{
		int next = Math.Clamp(parallelAmount, MIN_PARALLEL, MaxParallel);
		if (CurrentParallel == next) return;
		CurrentParallel = next;
		if (IsServer) MachineStateSyncPacket.Broadcast(this);
		// Re-search recipes - parallel ceiling changed.
		foreach (var controller in GetControllers())
		{
			if (controller is IRecipeLogicMachine rlm)
			{
				rlm.GetRecipeLogic().MarkLastRecipeDirty();
				rlm.GetRecipeLogic().UpdateTickSubscription();
			}
		}
	}

	public bool CanShared() => false;

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["currentParallel"] = CurrentParallel;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		MaxParallel = (int)Math.Pow(4, Tier - (int)VoltageTier.EV);
		CurrentParallel = tag.ContainsKey("currentParallel")
			? Math.Clamp(tag.GetInt("currentParallel"), MIN_PARALLEL, MaxParallel)
			: MaxParallel;
	}
}
