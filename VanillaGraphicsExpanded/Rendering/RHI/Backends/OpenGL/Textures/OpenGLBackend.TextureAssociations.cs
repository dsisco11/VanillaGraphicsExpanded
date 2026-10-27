using System.Collections.Immutable;
using GL = OpenTK.Graphics.OpenGL4.GL;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates separate texture objects retaining explicit original backing relationships.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Creates a realized buffer texture without re-executing failed association commands.</summary>
    public GpuTextureMetadata? InitializeBufferTexture(in GpuBufferTextureInitialization request, out nint id)
    {
        RequireContext();
        int name = 0;
        try { GL.CreateTextures(Native.TextureTarget.TextureBuffer, 1, out name); }
        finally { id = name; }
        RequireContext();
        if (name == 0) return null;
        bool whole = request.Mode == GpuBufferTextureMode.Whole;
        if (whole) GL.TextureBuffer(name, (Native.SizedInternalFormat)request.Format, request.Buffer);
        else GL.TextureBufferRange(name, (Native.SizedInternalFormat)request.Format, request.Buffer, request.Offset, request.Size);
        RequireContext();
        return new(OpenTK.Graphics.OpenGL.TextureTarget.TextureBuffer, ImmutableArray<GpuTextureLevelMetadata>.Empty,
            false, 0, 0, 0, new(request.Buffer, request.Format, whole ? 0 : request.Offset, whole ? 0 : request.Size, whole));
    }

    /// <summary>Reserves the untyped destination required by TextureView and derives view-relative facts.</summary>
    public GpuTextureMetadata? InitializeTextureView(in GpuTextureViewInitialization request, GpuTextureMetadata source, out nint id)
    {
        RequireContext();
        var candidate = TextureViewDescriptor.Create(request, source);
        // TextureView explicitly requires an unused generated name; CreateTextures would realize
        // it prematurely. This is its native creation contract, not a capability fallback.
        int name = 0;
        try { GL.GenTextures(1, out name); }
        finally { id = name; }
        RequireContext();
        if (name == 0) return null;
        GL.TextureView(name, (Native.TextureTarget)request.Target, request.Source, (Native.PixelInternalFormat)request.Format,
            request.BaseLevel, request.LevelCount, request.BaseLayer, request.LayerCount);
        RequireContext();
        return candidate;
    }
    #endregion
}
