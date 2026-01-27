using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

/// <summary>
/// GPU output stage.
///
/// Naming note:
/// The cache system uses a "store" concept. For GPU outputs, "store" means ensuring the payload is
/// staged (off-thread memcpy into already-mapped staging memory when available) and that the render-thread
/// commit is enqueued to the existing service loop.
///
/// Threading contract:
/// - Off-thread staging is allowed only for memcpy into already-mapped staging memory.
/// - Actual GL commits must be performed on the render thread via existing service loops.
/// </summary>
internal interface IArtifactGpuStore<TKey, TGpuPayload>
{
    ValueTask StoreAsync(ArtifactSession session, TKey key, in TGpuPayload payload);
}
