using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Projects established framebuffer descriptor selections without native rediscovery.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Returns selected image facts only from a live original backing and supplied subresource geometry.</summary>
    internal GpuImageMetadata? ProjectFramebufferDescriptor(GpuFramebufferImageMetadata descriptor)
    {
        var entry = descriptor.OriginalBacking;
        if (entry is null || !IsRegistered(entry) || !entry.IsUsable) return null;
        bool multiview = entry is GpuResourceMetadataEntry<GpuTextureMetadata> { Metadata.Target: TextureDimension.TwoArray }
            && descriptor.Mode != GpuFramebufferAttachmentMode.Ordinary && descriptor.ViewCount > 0
            && (descriptor.Layered || descriptor.Mode == GpuFramebufferAttachmentMode.Multiview);
        var selection = new GpuImageSelection(descriptor.MipLevel, descriptor.CubeFace == CubeFace.None ? descriptor.Layer : 0, descriptor.CubeFace,
            descriptor.Layered, multiview ? descriptor.BaseView : 0, multiview ? descriptor.ViewCount : 0);
        if (entry is GpuResourceMetadataEntry<GpuRenderbufferMetadata> renderbuffer)
            return ProjectRenderbufferImage(renderbuffer.Metadata, selection);
        return entry is GpuResourceMetadataEntry<GpuTextureMetadata> texture && IsNativeTextureViewSourceAvailable(texture)
            ? ProjectTextureImage(texture.Metadata, selection) : null;
    }
    #endregion
}
