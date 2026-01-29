using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal interface IChunkWorkItem
{
    ArtifactKey ArtifactKey { get; }

    Task ExecuteAsync(CancellationToken serviceCancellationToken);
}
