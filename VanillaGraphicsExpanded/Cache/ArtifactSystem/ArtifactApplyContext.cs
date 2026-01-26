using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Cache.Artifacts;

/// <summary>
/// Context provided to apply-stage logic.
/// Apply must be thread-affine (main thread and/or render thread depending on implementation).
/// </summary>
internal readonly record struct ArtifactApplyContext<TKey, TDiskPayload, TGpuPayload>(
    ICoreClientAPI Capi,
    ArtifactSession Session,
    TKey Key,
    Optional<TDiskPayload> DiskPayload,
    Optional<TGpuPayload> GpuPayload);
