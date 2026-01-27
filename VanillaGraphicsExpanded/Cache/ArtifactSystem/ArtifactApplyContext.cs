using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

/// <summary>
/// Context provided to apply-stage logic.
/// Apply must be thread-affine (main thread and/or render thread depending on implementation).
/// </summary>
internal readonly record struct ArtifactApplyContext<TKey, TOutput>(
    ICoreClientAPI Capi,
    ArtifactSession Session,
    TKey Key,
    Optional<TOutput> Output);