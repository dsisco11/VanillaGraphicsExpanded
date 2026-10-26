using System;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Captures actual renderbuffer storage while preserving the caller's binding.</summary>
internal readonly struct OpenGLRenderbufferQueries
{
    private readonly OpenGLBackend backend;

    #region Internal API
    /// <summary>Retains the original context for every external read.</summary>
    internal OpenGLRenderbufferQueries(OpenGLBackend backend) => this.backend = backend;

    #endregion

    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => id != 0 && id >= int.MinValue && id <= uint.MaxValue && GL.IsRenderbuffer(unchecked((int)id));

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuRenderbufferMetadata metadata)
    {
        backend.RequireContext();
        metadata = default;
        if (!IsResource(id)) return false;
        int width, height, format, samples;
        int name = unchecked((int)id);
        GL.GetNamedRenderbufferParameter(name, Native.RenderbufferParameterName.RenderbufferWidth, out width);
        backend.RequireContext();
        GL.GetNamedRenderbufferParameter(name, Native.RenderbufferParameterName.RenderbufferHeight, out height);
        backend.RequireContext();
        GL.GetNamedRenderbufferParameter(name, Native.RenderbufferParameterName.RenderbufferInternalFormat, out format);
        backend.RequireContext();
        GL.GetNamedRenderbufferParameter(name, Native.RenderbufferParameterName.RenderbufferSamples, out samples);
        backend.RequireContext();
        if (width < 0 || height < 0 || format <= 0 || samples < 0) return false;
        metadata = new(width, height, (PixelInternalFormat)format, samples);
        return true;
    }
    #endregion
}
