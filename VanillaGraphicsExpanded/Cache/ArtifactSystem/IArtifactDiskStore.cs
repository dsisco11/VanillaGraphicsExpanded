using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

/// <summary>
/// Off-thread disk persistence stage.
///
/// Naming note:
/// The broader cache system uses a "store" concept (see <see cref="VanillaGraphicsExpanded.Cache.ICacheStore"/>).
/// This interface is a higher-level store for typed payloads, typically implemented by wrapping an
/// <see cref="VanillaGraphicsExpanded.Cache.IDataCacheSystem{TKey,TPayload}"/> or an <see cref="VanillaGraphicsExpanded.Cache.ICacheStore"/>
/// plus a codec.
///
/// Option B semantics: writes must not be dropped; backpressure must be enforced by scheduler admission.
/// </summary>
internal interface IArtifactDiskStore<TKey, TDiskPayload>
{
    ValueTask StoreAsync(ArtifactSession session, TKey key, in TDiskPayload payload);
}
