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
        id = GL.CreateShader(NativeShaderStage(initialization.Stage));
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

    #region Private
    /// <summary>Completes an unpublished program allocation for shader realization or explicit engine integration.</summary>
    private GpuProgramObject CreateProgramOwner(string? label)
    {
        RequireContext();
        var owner = new GpuProgramObject();
        try
        {
            if (!GpuResourceManager.CreateProgram(new(), owner, out _).HasValue)
                throw new System.InvalidOperationException("Program allocation failed.");
            owner.SetDebugName(label);
            return owner;
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    #endregion
}
