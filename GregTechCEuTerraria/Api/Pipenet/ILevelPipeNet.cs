#nullable enable
namespace GregTechCEuTerraria.Api.Pipenet;

// Non-generic-on-TNet view of LevelPipeNet - the surface PipeNet<TData>
// needs to call on its owning level container. Exists because C# doesn't
// give us covariance on closed generic classes: a `LevelPipeNet<TData,
// MySpecificNet>` can't be assigned to `LevelPipeNet<TData, PipeNet<TData>>`
// even though `MySpecificNet : PipeNet<TData>`. PipeNet stores a reference
// of this interface type so subclasses with their own TNet can still wire
// up cleanly.
public interface ILevelPipeNet<TData> where TData : notnull
{
	void SetDirty();

	void AddPipeNetToChunk((int cx, int cy) chunkPos, PipeNet<TData> pipeNet);
	void RemovePipeNetFromChunk((int cx, int cy) chunkPos, PipeNet<TData> pipeNet);

	PipeNet<TData>? GetNetFromPos((int x, int y) pos);
	PipeNet<TData> CreateNetInstance();
	void AddPipeNet(PipeNet<TData> pipeNet);
	void RemovePipeNet(PipeNet<TData> pipeNet);
}
