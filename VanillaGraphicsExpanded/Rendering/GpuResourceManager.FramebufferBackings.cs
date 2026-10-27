using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires reusable backing snapshots for framebuffer descriptor reconciliation.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Uses available covered texture facts unless the external owner explicitly requests recovery.</summary>
    internal static GpuTextureMetadata AcquireFramebufferTexture(int id, TextureDimension target, bool recover)
    {
        OpenGlStateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.Texture, id);
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata == true)
        {
            var reader = new GpuTextureMetadataReader(target);
            var entry = recover ? manager.RecoverResource(GpuResourceKind.Texture, id, reader)
                : manager.AcquireResource(GpuResourceKind.Texture, id, reader);
            return entry?.Metadata ?? throw new InvalidOperationException("Framebuffer texture metadata is unavailable.");
        }
        if (!new GpuTextureMetadataReader(target).TryRead(id, out var result))
            throw new InvalidOperationException("Framebuffer texture metadata is unavailable.");
        return result;
    }

    /// <summary>Uses one renderbuffer snapshot for every image selection in a framebuffer discovery.</summary>
    internal static GpuRenderbufferMetadata AcquireFramebufferRenderbuffer(int id, bool recover)
    {
        OpenGlStateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.Renderbuffer, id);
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata == true)
        {
            var entry = recover ? manager.RecoverResource(GpuResourceKind.Renderbuffer, id, GpuRenderbufferMetadataReader.Instance)
                : manager.AcquireResource(GpuResourceKind.Renderbuffer, id, GpuRenderbufferMetadataReader.Instance);
            return entry?.Metadata ?? throw new InvalidOperationException("Framebuffer renderbuffer metadata is unavailable.");
        }
        if (!GpuRenderbufferMetadataReader.Instance.TryRead(id, out var result))
            throw new InvalidOperationException("Framebuffer renderbuffer metadata is unavailable.");
        return result;
    }
    #endregion
}
