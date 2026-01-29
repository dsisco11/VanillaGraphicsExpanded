using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public interface IChunkProcessingService
{
    /// <summary>
    /// Requests computation of an artifact for <paramref name="key"/> at <paramref name="version"/>.
    /// </summary>
    /// <remarks>
    /// Expected outcomes (superseded, canceled, unavailable) are reported via <see cref="ChunkWorkResult{TArtifact}"/>
    /// rather than exceptions.
    /// </remarks>
    Task<ChunkWorkResult<TArtifact>> RequestAsync<TArtifact>(
        ChunkKey key,
        int version,
        IChunkProcessor<TArtifact> processor,
        ChunkWorkOptions? options = null,
        CancellationToken ct = default);
}
