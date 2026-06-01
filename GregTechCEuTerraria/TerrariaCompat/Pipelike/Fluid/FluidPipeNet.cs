#nullable enable
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Pipenet;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;

// Verbatim port of FluidPipeNet. No route cache - upstream walks cardinal
// neighbours per pipe-cell tick (FluidPipeBlockEntity.distributeFluid).
public sealed class FluidPipeNet : PipeNet<FluidPipeProperties>
{
	public FluidPipeNet(ILevelPipeNet<FluidPipeProperties> level) : base(level) { }

	protected override void WriteNodeData(FluidPipeProperties nodeData, TagCompound tag)
	{
		tag["max_temperature"] = nodeData.MaxFluidTemperature;
		tag["throughput"]      = nodeData.Throughput;
		tag["gas_proof"]       = nodeData.GasProof;
		tag["acid_proof"]      = nodeData.AcidProof;
		tag["cryo_proof"]      = nodeData.CryoProof;
		tag["plasma_proof"]    = nodeData.PlasmaProof;
		tag["channels"]        = nodeData.Channels;
	}

	protected override FluidPipeProperties ReadNodeData(TagCompound tag) => new()
	{
		MaxFluidTemperature = tag.GetInt("max_temperature"),
		Throughput          = tag.GetInt("throughput"),
		GasProof            = tag.GetBool("gas_proof"),
		AcidProof           = tag.GetBool("acid_proof"),
		CryoProof           = tag.GetBool("cryo_proof"),
		PlasmaProof         = tag.GetBool("plasma_proof"),
		Channels            = tag.GetInt("channels"),
	};
}
