using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Establishes shader and program creation facts before compilation or linking.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Allocates a fixed-stage shader and returns only its creation-established descriptor.</summary>
    internal GpuShaderMetadata? CreateShader(in GpuShaderInitialization initialization, out nint id)
    {
        id = GL.CreateShader(initialization.Stage);
        return id == 0 ? null : new GpuShaderMetadata(initialization.Stage);
    }

    /// <summary>Allocates an unlinked program with empty interface collections.</summary>
    internal GpuProgramMetadata? CreateProgram(out nint id)
    {
        id = GL.CreateProgram();
        return id == 0 ? null : new GpuProgramMetadata(false,
            ImmutableArray<GpuProgramResourceMetadata>.Empty, ImmutableArray<GpuProgramTextureUnitMetadata>.Empty);
    }
    #endregion
}
