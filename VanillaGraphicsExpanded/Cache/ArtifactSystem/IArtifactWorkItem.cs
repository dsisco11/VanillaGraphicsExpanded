namespace VanillaGraphicsExpanded.Cache.Artifacts;

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
}
