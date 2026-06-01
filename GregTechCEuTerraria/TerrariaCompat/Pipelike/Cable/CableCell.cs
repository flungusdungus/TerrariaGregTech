#nullable enable
using GregTechCEuTerraria.Common.Energy;
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// One placed cable. Material-derived fields denormalised at placement so
// the layer + graph + sim stay MaterialRegistry-free. WireSize in {1,2,4,8,16}
// (single/double/quadruple/octal/hex). Insulated = rubber-coated cable.
public readonly record struct CableCell(
	string MaterialId,
	byte WireSize,
	bool Insulated,
	VoltageTier Voltage,
	int BaseAmperage,
	int LossPerAmp)
{
	public int TotalAmperage => BaseAmperage * WireSize;
}
