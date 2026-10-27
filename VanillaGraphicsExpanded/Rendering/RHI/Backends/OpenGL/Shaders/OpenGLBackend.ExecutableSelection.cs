using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Selects fixed executables for graphics and compute using the cache's single program binding knowledge.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Selects an executable only when its full program binding is unknown or different.</summary>
    private void ApplyExecutable(RHIShader shader)
    {
        Integration.NativeShaderHandoff.RequireInactive();
        OpenGLProgramSelection.Select(cache,shader.Program.ProgramId);
        RequireContext();
    }
    #endregion
}
