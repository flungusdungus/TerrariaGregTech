#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover.Data;

namespace GregTechCEuTerraria.Api.Cover;

// Port of com.gregtechceu.gtceu.api.cover.IIOCover.
//
// Marker + config surface for transfer covers - conveyor, pump, robot arm,
// fluid regulator. The actual per-tick transfer is pipe/auto-IO dependent and
// stubbed until pipes are ported; the cover still carries these settings.
public interface IIOCover
{
	int TransferRate { get; }

	IO Io { get; }

	ManualIOMode ManualIOMode { get; }
}
