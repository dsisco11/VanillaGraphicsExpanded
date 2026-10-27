using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains the original realized array and the configuration revision certified by an explicit external importer.</summary>
internal sealed class RHIGeometryImport
{
    private readonly GpuResourceRegistration? association;
    private readonly ulong revision, localEpoch;

    #region Internal API
    /// <summary>Gets a nonowning recipient for an already configured native array.</summary>
    internal GpuResource<GpuVertexArrayMetadata> Array { get; }
    /// <summary>Gets the importer-established originating native namespace.</summary>
    internal (nint Handle, long Generation) Origin { get; }
    /// <summary>Captures only completed certified identity; this never queries or recovers external configuration.</summary>
    internal RHIGeometryImport(GpuResource<GpuVertexArrayMetadata> array, (nint Handle, long Generation) origin, ulong localEpoch)
    {
        Array = array;
        Origin = origin;
        association = array.ResourceManager?.GetResourceRegistration(array);
        revision = association?.MetadataRevision ?? 0;
        this.localEpoch = localEpoch;
    }
    /// <summary>Rejects changed, retired or wrong-context original configuration without issuing a native query.</summary>
    internal void RequireCurrent((nint Handle, long Generation) origin, ulong epoch)
    {
        if (!IsCurrent(origin, epoch))
            throw new InvalidOperationException("Imported geometry no longer has its certified original array, configuration or context.");
    }
    /// <summary>Allows an external producer to refresh a changed receipt before entering a submission boundary.</summary>
    internal bool IsCurrent((nint Handle, long Generation) origin, ulong epoch) => Origin == origin && Array.IsValid
        && ReferenceEquals(association, Array.ResourceManager?.GetResourceRegistration(Array))
        && (association is not null ? association.MetadataRevision == revision && Array.ResourceManager?.IsResourceMetadataAvailable(Array) == true
            : epoch == localEpoch);
    #endregion
}
