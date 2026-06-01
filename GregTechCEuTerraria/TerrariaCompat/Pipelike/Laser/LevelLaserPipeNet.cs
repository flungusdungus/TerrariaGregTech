#nullable enable
using GregTechCEuTerraria.Api.Pipenet;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// Concrete LevelPipeNet for laser pipes - mirrors LevelItemPipeNet /
// LevelFluidPipeNet shape. The Save/Load story matches our existing pipe
// nets: the level instance is derived from the LaserPipeLayer's cells on
// world load (no separate SavedData blob), so this class is just the typed
// net-instance factory.
public sealed class LevelLaserPipeNet : LevelPipeNet<LaserPipeProperties, LaserPipeNet>
{
	protected internal override LaserPipeNet CreateNetInstance() => new(this);
}
