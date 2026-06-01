#nullable enable
using GregTechCEuTerraria.Api.Cover.Data;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// UI-read surface shared by RobotArmCover / FluidRegulatorCover - lets the
// cover settings popup read the transfer mode + per-type limit uniformly
// without naming the concrete cover. Mutation goes through ApplySetting.
public interface ITransferModeCover
{
	TransferMode TransferMode { get; }
	int GlobalTransferLimit { get; }
}
