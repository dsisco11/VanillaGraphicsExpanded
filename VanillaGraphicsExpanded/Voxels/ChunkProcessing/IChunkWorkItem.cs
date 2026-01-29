using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal interface IChunkWorkItem
{
    ArtifactKey ArtifactKey { get; }

    ChunkProcessorKey ChunkProcessorKey { get; }

    int Version { get; }

    bool TryCompleteSuperseded(string reason);

    Task ExecuteAsync(CancellationToken serviceCancellationToken);
}
