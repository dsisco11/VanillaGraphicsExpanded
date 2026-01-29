using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public interface IChunkProcessor<TArtifact>
{
    /// <summary>
    /// Stable, low-cardinality identifier for caching, deduplication, and profiling.
    /// </summary>
    /// <remarks>
    /// Keep this intentionally bucketed (e.g., "Sdf.R16" not "Sdf.R16.Seed1234").
    /// </remarks>
    string Id { get; }

    ValueTask<TArtifact> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct);
}

