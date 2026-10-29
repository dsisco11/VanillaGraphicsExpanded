using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires reusable backing snapshots for framebuffer descriptor reconciliation.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Uses the available canonical texture facts for an attachment.</summary>
    internal static GpuTextureMetadata AcquireFramebufferTexture(int id, TextureDimension target)
    {
        RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Texture, id);
        var manager = RHIResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata == true)
        {
            var reader = new GpuTextureMetadataReader(target);
            var entry = manager.AcquireResource(GpuResourceKind.Texture, id, reader);
            return entry?.Metadata ?? throw new InvalidOperationException("Framebuffer texture metadata is unavailable.");
        }
        throw new InvalidOperationException("Framebuffer texture requires maintained backing facts.");
    }

    /// <summary>Uses one renderbuffer snapshot for every image selection in a framebuffer discovery.</summary>
    internal static GpuRenderbufferMetadata AcquireFramebufferRenderbuffer(int id)
    {
        RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Renderbuffer, id);
        var manager = RHIResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata == true)
        {
            var entry = manager.AcquireResource(GpuResourceKind.Renderbuffer, id, GpuRenderbufferMetadataReader.Instance);
            return entry?.Metadata ?? throw new InvalidOperationException("Framebuffer renderbuffer metadata is unavailable.");
        }
        throw new InvalidOperationException("Framebuffer renderbuffer requires maintained backing facts.");
    }
    #endregion
}
