using VanillaGraphicsExpanded.Rendering.RHI;
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Selects existing entries for native command observation within their owning context.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Identifies the active native context without moving a registry between contexts.</summary>
    internal bool CanObserveNativeMetadata => !IsMetadataRegistryRetired && context != default && context == currentMetadataContext();

    /// <summary>Ignores untracked objects and unrelated contexts without creating speculative registrations.</summary>
    internal GpuResourceRegistration? FindObservedResource(GpuResourceKind kind, nint id)
    {
        if (id == 0 || !CanObserveNativeMetadata) return null;
        RequireMetadataContext();
        // Looking up established facts is not command admission. The external mutation's
        // observer explicitly interrupts its own affected operation before changing facts;
        // unrelated backing lookups must neither veto dispatch nor interrupt their readers.
        var entry = FindResource(kind, id);
        return entry?.State == GpuResourceRegistrationState.Live ? entry : null;
    }
    #endregion
}
