using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates realized core vertex arrays through the existing manager's private allocation handoff.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Allocates an empty realized array without changing selectors or reading unrepresented descriptor fields.</summary>
    public GpuVertexArrayMetadata? CreateVertexArray(out nint id)
    {
        RequireContext();
        OpenGLDriverCalls.CreateVertexArrays(1, out int name);
        id = name;
        RequireContext();
        if (name != 0) cache.EstablishVertexArrayElementKnowledge(name, 0);
        return name == 0 ? null : new GpuVertexArrayMetadata();
    }
    #endregion

    #region Private
    /// <summary>Completes a private geometry recipient before any configuration or cache membership can expose it.</summary>
    private GpuVao CreateGeometryVertexArray()
    {
        RequireContext();
        var owner = new GpuVao();
        try
        {
            if (!RHIResourceManager.CreateVertexArray(new(), owner, out _).HasValue)
                throw new InvalidOperationException("Vertex-array creation failed.");
            owner.SetDebugName("RHI.Geometry");
            return owner;
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    #endregion
}
