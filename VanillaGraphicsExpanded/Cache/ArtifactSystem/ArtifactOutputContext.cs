using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Cache.Artifacts;

/// <summary>
/// Context provided to the output stage.
/// Runs off-thread and may dispatch disk and/or GPU storage work.
/// </summary>
internal readonly record struct ArtifactOutputContext<TKey, TOutput>(
    ICoreClientAPI Capi,
    ArtifactSession Session,
    TKey Key,
    TOutput Output);
