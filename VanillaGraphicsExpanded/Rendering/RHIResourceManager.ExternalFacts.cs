namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Authorizes explicit external scene-resource discovery, reusing covered canonical facts.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Acquires unknown external facts without forcing discovery for an available registry entry.</summary>
    internal static T? AcquireExternalFacts<T>(GpuResourceKind kind, int id, IGpuResourceMetadataReader<T> reader) where T : struct
    {
        if (id <= 0) return null;
        if (RHIResourceManagerSystem.CurrentManager is { IsMetadataRegistryRetired: false } manager)
            return manager.AcquireResource(kind, id, reader)?.Metadata;
        return null;
    }
    #endregion
}
