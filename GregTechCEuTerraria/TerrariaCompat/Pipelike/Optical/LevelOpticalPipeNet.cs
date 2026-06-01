#nullable enable
using GregTechCEuTerraria.Api.Pipenet;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// Concrete LevelPipeNet for optical pipes - mirrors LevelLaserPipeNet shape.
// The level instance is derived from the OpticalPipeLayer's cells on world
// load (no separate SavedData blob), so this class is just the typed
// net-instance factory.
public sealed class LevelOpticalPipeNet : LevelPipeNet<OpticalPipeProperties, OpticalPipeNet>
{
	protected internal override OpticalPipeNet CreateNetInstance() => new(this);
}
