using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Supplies native queries only for explicit external acquisition or narrowly selected output facts.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Reads unknown image selectors for the existing state cache without acquiring resource descriptors.</summary>
    internal int ReadImageSelector(GetPName parameter)
    {
        RequireContext();
        int value = GL.GetInteger((Native.GetPName)parameter);
        RequireContext();
        return value;
    }

    /// <summary>Reads unknown external image descriptors without changing controlled-allocation policy.</summary>
    public bool ReadExternalTexture(nint id, TextureDimension target, out GpuTextureMetadata metadata)
    {
        RequireContext();
        if (!System.Enum.IsDefined(target)) { metadata = default; return false; }
        bool result = new OpenGLTextureQueries(target == TextureDimension.Undefined ? 0 : OpenGLImageValues.Target(target), this).TryRead(id, out metadata);
        RequireContext();
        return result;
    }
    /// <summary>Reads unknown external renderbuffer storage using core named queries.</summary>
    public bool ReadExternalRenderbuffer(nint id, out GpuRenderbufferMetadata metadata)
    {
        RequireContext();
        bool result = new OpenGLRenderbufferQueries(this).TryRead(id, out metadata);
        RequireContext();
        return result;
    }

    /// <summary>Reads the optional renderbuffer-backed texture association through its required context selector.</summary>
    public bool ReadExternalRenderbufferTexture(int texture, out GpuTextureMetadata metadata)
    {
        RequireContext();
        metadata = default;
        if (!GpuSupport.Graphics.SupportsRenderbufferTextures) return false;
        var target = (TextureTarget)All.TextureRenderbufferNv;
        using var binding = cache.BindTextureScope(target, 0, texture);
        GL.GetInteger((Native.GetPName)All.TextureRenderbufferDataStoreBindingNv, out int backing);
        RequireContext();
        if (backing < 0) return false;
        metadata = new(TextureDimension.RenderbufferTexture, System.Collections.Immutable.ImmutableArray<GpuTextureLevelMetadata>.Empty, false, 0, 0, 0, Renderbuffer: backing);
        return true;
    }
    #endregion
}
