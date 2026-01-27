using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

/// <summary>
/// Context provided to worker-thread artifact computation.
/// Threading contract: worker threads are allowed to load/decode assets and perform CPU work.
/// </summary>
internal readonly record struct ArtifactComputeContext<TKey>(
    ICoreClientAPI Capi,
    ArtifactSession Session,
    TKey Key);
