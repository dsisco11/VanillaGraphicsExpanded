namespace VanillaGraphicsExpanded.Cache.Artifacts;

/// <summary>
/// Result of computing a cache artifact.
/// The scheduler will invoke a single output stage after compute.
/// </summary>
internal readonly record struct ArtifactComputeResult<TOutput>(
    bool IsNoop,
    Optional<TOutput> Output,
    bool RequiresApply);
