namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Stores the synchronized native metadata snapshot of a resource wrapper.</summary>
public abstract class GpuResource<TMetadata> : GpuResource where TMetadata : struct
{
    private TMetadata metadataSnapshot;

    /// <summary>Supplies the published snapshot to manager-owned consumers without acquisition.</summary>
    internal TMetadata PublishedMetadata => metadataSnapshot;

    #region Public API
    /// <summary>Replaces this wrapper's snapshot without querying native state or altering ownership.</summary>
    public void HandleMetadataChanged(TMetadata data)
    {
        metadataSnapshot = data;
    }
    #endregion

    #region Protected API
    /// <summary>Exposes the manager-published snapshot without allowing derived wrappers to edit it.</summary>
    protected ref readonly TMetadata metadata => ref metadataSnapshot;
    #endregion
}
