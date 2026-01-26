namespace VanillaGraphicsExpanded.Cache.Artifacts;

/// <summary>
/// Result of computing a cache artifact.
/// The scheduler may emit disk and/or GPU outputs, and may optionally apply results back to game-visible state.
/// </summary>
internal readonly record struct ArtifactComputeResult<TDiskPayload, TGpuPayload>(
    bool IsNoop,
    Optional<TDiskPayload> DiskPayload,
    Optional<TGpuPayload> GpuPayload,
    bool RequiresApply);
