#nullable enable
using GregTechCEuTerraria.Api.Pipenet;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// Concrete LevelPipeNet for the LD pipe layer - mirrors LevelLaserPipeNet /
// LevelItemPipeNet. Derived from the LongDistancePipeLayer cells on world load
// (no separate SavedData blob), so this is just the typed net-instance factory.
public sealed class LevelLongDistancePipeNet : LevelPipeNet<LongDistancePipeProperties, LongDistancePipeNet>
{
	protected internal override LongDistancePipeNet CreateNetInstance() => new(this);
}
