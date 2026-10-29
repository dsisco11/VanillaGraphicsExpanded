namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Exposes the base image of the manager-published texture snapshot.</summary>
public abstract partial class GpuTexture
{
    #region Internal API
    /// <summary>Exposes the received snapshot for manager-owned command input validation and composition.</summary>
    internal GpuTextureMetadata MetadataSnapshot => metadata;
    #endregion

    #region Private
    /// <summary>Gets a declaration-safe base image before native allocation or after snapshot publication.</summary>
    private GpuTextureLevelMetadata BaseImage
    {
        get
        {
            // A retained snapshot is not an available image after an interrupted
            // operation or retirement, even though its last values remain stored.
            if (ResourceManager?.GetResourceRegistration(this) is { IsUsable: false }) return default;
            if (!metadata.Levels.IsDefault)
                foreach (var image in metadata.Levels)
                    if (image.Level == 0) return image;
            return default;
        }
    }

    #endregion
}
