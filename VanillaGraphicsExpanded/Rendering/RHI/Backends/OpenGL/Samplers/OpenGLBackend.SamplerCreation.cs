using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Realizes sampler objects without disturbing texture-unit selectors.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Creates the sampler candidate for private manager handoff.</summary>
    public GpuSamplerMetadata? InitializeSampler(out nint id)
    {
        RequireContext();
        int name = 0;
        try { GL.CreateSamplers(1, out name); }
        finally { id = name; }
        RequireContext();
        return name == 0 ? null : new GpuSamplerMetadata();
    }
    #endregion
}
