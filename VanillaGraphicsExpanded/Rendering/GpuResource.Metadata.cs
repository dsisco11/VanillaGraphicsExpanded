namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Stores the synchronized native metadata snapshot of a resource wrapper.</summary>
public abstract class GpuResource<TMetadata> : GpuResource where TMetadata : struct
{
    /// <summary>Contains the last complete snapshot assigned by the resource manager.</summary>
    protected TMetadata metadata;

    /// <summary>Stores the image selection used to project canonical backing metadata.</summary>
    internal GpuImageSelection? MetadataImageSelection;

    #region Public API
    /// <summary>Replaces this wrapper's snapshot without querying native state or altering ownership.</summary>
    public void HandleMetadataChanged(TMetadata data)
    {
        metadata = data;
    }
    #endregion
}
