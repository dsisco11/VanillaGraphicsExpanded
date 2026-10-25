using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Supplies available original texture facts before an operation withdraws its result.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Uses published storage when available and explicitly recovers only missing input facts.</summary>
    internal static GpuTextureMetadata AcquireTextureCommandInput(GpuTexture texture)
    {
        StateCache.Current.RequireResourceOperationAvailable(texture.Kind, texture.ResourceId);
        if (texture.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(texture))
        {
            manager.RequireOriginalWrapper(texture);
            var entry = manager.GetResourceRegistration(texture) as GpuResourceMetadataEntry<GpuTextureMetadata>;
            if (entry is not null && manager.IsResourceMetadataAvailable(texture)) return entry.Metadata;
            AcquireWrapperMetadata(texture, new GpuTextureMetadataReader(texture.TextureTarget));
        }
        else if (texture.StorageImages.IsDefaultOrEmpty)
            AcquireWrapperMetadata(texture, new GpuTextureMetadataReader(texture.TextureTarget));
        if (texture.StorageImages.IsDefaultOrEmpty)
            throw new InvalidOperationException("Texture command input metadata is unavailable.");
        return texture.MetadataSnapshot;
    }
    #endregion
}
