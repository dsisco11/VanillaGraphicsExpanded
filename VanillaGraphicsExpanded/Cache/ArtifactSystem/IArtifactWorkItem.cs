namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

/// <summary>
/// A unit of work identified by a key.
/// De-dup invariant (scheduler-owned): at most one queued instance per key.
/// </summary>
internal interface IArtifactWorkItem<TKey>
{
    TKey Key { get; }

    /// <summary>
    /// Higher value = higher priority.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Logical generator/type identifier (e.g., "BaseColor", "MaterialParams", "NormalDepth").
    /// </summary>
    string TypeId { get; }

    string DebugLabel { get; }

    /// <summary>
    /// Declares which output pipelines this work item will use if executed.
    /// Used by the scheduler to enforce Option B backpressure during admission.
    /// </summary>
    ArtifactOutputKinds RequiredOutputKinds { get; }
}
