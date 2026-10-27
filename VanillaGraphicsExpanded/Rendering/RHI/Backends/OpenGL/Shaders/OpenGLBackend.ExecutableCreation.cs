using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Establishes shader and program creation facts before compilation or linking.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Allocates a fixed-stage shader and returns only its creation-established descriptor.</summary>
    public GpuShaderMetadata? InitializeShader(in GpuShaderInitialization initialization, out nint id)
    {
        RequireContext();
        id = GL.CreateShader((ShaderType)initialization.Stage);
        RequireContext();
        return id == 0 ? null : new GpuShaderMetadata(initialization.Stage);
    }

    /// <summary>Allocates an unlinked program with empty interface collections.</summary>
    public GpuProgramMetadata? InitializeProgram(out nint id)
    {
        RequireContext();
        id = GL.CreateProgram();
        RequireContext();
        return id == 0 ? null : new GpuProgramMetadata(false,
            ImmutableArray<GpuProgramResourceMetadata>.Empty, ImmutableArray<GpuProgramTextureUnitMetadata>.Empty);
    }
    #endregion
}
