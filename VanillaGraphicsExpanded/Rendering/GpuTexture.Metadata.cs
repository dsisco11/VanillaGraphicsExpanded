namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Exposes the base image of the manager-published texture snapshot.</summary>
public abstract partial class GpuTexture
{
    #region Private
    /// <summary>Gets a declaration-safe base image before native allocation or after snapshot publication.</summary>
    private GpuTextureLevelMetadata BaseImage
    {
        get
        {
            if (!metadata.Levels.IsDefault)
                foreach (var image in metadata.Levels)
                    if (image.Level == 0) return image;
            return default;
        }
    }

    #endregion
}
