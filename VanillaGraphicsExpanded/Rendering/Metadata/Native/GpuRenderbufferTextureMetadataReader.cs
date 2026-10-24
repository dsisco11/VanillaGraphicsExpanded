using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Reads renderbuffer texture associations without issuing unsupported texture image queries.</summary>
internal static class GpuRenderbufferTextureMetadataReader
{
    #region Internal API
    /// <summary>Captures the backing name; renderbuffer textures have no mip levels or texture parameters.</summary>
    internal static bool TryRead(int texture, out GpuTextureMetadata metadata)
    {
        metadata = default;
        if (!GpuSupport.Graphics.SupportsRenderbufferTextures) return false;
        var target = (TextureTarget)All.TextureRenderbufferNv; // GL_TEXTURE_RENDERBUFFER_NV
        using var binding = StateCache.Current.BindTextureScope(target, 0, texture);
        int backing = -1;
        // This target exposes its association through context state, not GetTexParameter
        // or GetTexLevelParameter. Its dimensions remain properties of the renderbuffer.
        GL.GetInteger((GetPName)All.TextureRenderbufferDataStoreBindingNv, out backing);
        if (backing < 0) return false;
        metadata = new(target, ImmutableArray<GpuTextureLevelMetadata>.Empty,
            false, 0, 0, 0, Renderbuffer: backing);
        return true;
    }
    #endregion
}
