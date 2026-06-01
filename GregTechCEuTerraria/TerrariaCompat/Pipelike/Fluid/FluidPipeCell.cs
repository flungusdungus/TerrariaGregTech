#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;

// One placed fluid-pipe. Material-derived fields baked off Material.FluidPipe
// at placement so the layer + sim stay MaterialRegistry-free.
public readonly record struct FluidPipeCell(
	string MaterialId,
	PipeSize Size,
	int Throughput,
	int Channels,
	int MaxFluidTemperature,
	bool GasProof,
	bool CryoProof,
	bool PlasmaProof,
	bool AcidProof,
	// See ItemPipeCell.IsSimple for the full rationale - same shape.
	bool IsSimple = false);
