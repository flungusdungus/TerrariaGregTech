#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// One placed item-pipe. Material-keyed, denormalised at placement.
// (Priority, TransferRate) bake in upstream's modifyProperties(material x size)
// so routing stays MaterialRegistry-free at tick time. Restrictive is carried
// separately because the route cache splits on it.
public readonly record struct ItemPipeCell(
	string MaterialId,
	PipeSize Size,
	bool Restrictive,
	int Priority,
	float TransferRate,
	// Drives the simple-pipe UI + place defaults; routing/throughput unaffected.
	// Sentinel MaterialId ("simple_item" / "simple_fluid") keeps them in their
	// own net via MaterialMark.
	bool IsSimple = false);
