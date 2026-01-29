using System;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public sealed class ChunkProcessingServiceOptions
{
    public int WorkerCount { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public long ArtifactCacheBudgetBytes { get; init; } = 64L * 1024 * 1024;
}
