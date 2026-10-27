using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates realized core vertex arrays through the existing manager's private allocation handoff.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Allocates an empty realized array without changing selectors or reading unrepresented descriptor fields.</summary>
    internal GpuVertexArrayMetadata? CreateVertexArray(out nint id)
    {
        RequireContext();
        GL.CreateVertexArrays(1, out int name);
        id = name;
        RequireContext();
        return name == 0 ? null : new GpuVertexArrayMetadata();
    }
    #endregion
}
