using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Supplies available original texture facts before an operation withdraws its result.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Requires command-established storage facts without initiating discovery or recovery.</summary>
    internal static GpuTextureMetadata AcquireTextureCommandInput(GpuTexture texture)
    {
        RHICommandContext.RequireResourceOperationAvailable(texture.Kind, texture.ResourceId);
        if (texture.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(texture))
        {
            manager.RequireOriginalWrapper(texture);
            var entry = manager.GetResourceRegistration(texture) as GpuResourceMetadataEntry<GpuTextureMetadata>;
            if (entry is not null && manager.IsResourceMetadataAvailable(texture)) return entry.Metadata;
            throw new InvalidOperationException("Texture command requires available original metadata.");
        }

        if (texture.StorageImages.IsDefaultOrEmpty)
            throw new InvalidOperationException("Texture command input metadata is unavailable.");
        return texture.MetadataSnapshot;
    }
    #endregion
}
