using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Explicitly acquires complete operation results for command families retaining native recovery.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Reads a completed texture operation through its established target-specific reader.</summary>
    internal static GpuTextureMetadata ReadTextureOperationResult(GpuTexture texture, TextureTarget target)
    {
        var entry = texture.ResourceManager?.GetResourceRegistration(texture) as GpuResourceMetadataEntry<GpuTextureMetadata>;
        var reader = entry is null ? new GpuTextureMetadataReader(target) : entry.Reader ??= new GpuTextureMetadataReader(target);
        if (!reader.TryRead(texture.ResourceId, out var candidate))
            throw new InvalidOperationException("Texture operation metadata recovery failed.");
        return candidate;
    }

    /// <summary>Reads a completed attachment operation without issuing publication or retrying commands.</summary>
    internal static GpuFramebufferMetadata ReadFramebufferOperationResult(GpuFramebuffer framebuffer)
    {
        var entry = framebuffer.ResourceManager?.GetResourceRegistration(framebuffer) as GpuResourceMetadataEntry<GpuFramebufferMetadata>;
        var reader = entry is null ? GpuFramebufferMetadataReader.Instance : entry.Reader ??= GpuFramebufferMetadataReader.Instance;
        if (!reader.TryRead(framebuffer.ResourceId, out var candidate))
            throw new InvalidOperationException("Framebuffer operation metadata recovery failed.");
        return candidate;
    }
    #endregion
}
