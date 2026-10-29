using System.Collections.Generic;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Records engine-published image identities before native observation begins.</summary>
internal static class EnginePreexistingImages
{
    #region Internal API
    /// <summary>Records only live framebuffer and texture names supplied by existing engine references.</summary>
    internal static void Record(RHIResourceManager manager, IEnumerable<FrameBufferRef> framebuffers)
    {
        foreach (var framebuffer in framebuffers)
        {
            if (framebuffer is null || framebuffer.Disposed) continue;
            RecordFramebuffer(manager, framebuffer.FboId);
            RecordTexture(manager, framebuffer.DepthTextureId);
            if (framebuffer.ColorTextureIds is { } colors)
                foreach (int texture in colors) RecordTexture(manager, texture);
            if (framebuffer.FbAttrs?.Attachments is { } attachments)
                foreach (var attachment in attachments)
                    if (attachment?.Texture is { } texture) RecordTexture(manager, texture.TextureId);
        }
    }
    #endregion

    #region Private
    /// <summary>Deduplicates an engine framebuffer identity without probing native descriptors.</summary>
    private static void RecordFramebuffer(RHIResourceManager manager, int id)
    {
        if (id > 0 && manager.FindResource(GpuResourceKind.Framebuffer, id) is null)
            manager.RecordPreexistingImage<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer, id);
    }

    /// <summary>Deduplicates an engine texture identity without probing native descriptors.</summary>
    private static void RecordTexture(RHIResourceManager manager, int id)
    {
        if (id > 0 && manager.FindResource(GpuResourceKind.Texture, id) is null)
            manager.RecordPreexistingImage<GpuTextureMetadata>(GpuResourceKind.Texture, id);
    }
    #endregion
}
