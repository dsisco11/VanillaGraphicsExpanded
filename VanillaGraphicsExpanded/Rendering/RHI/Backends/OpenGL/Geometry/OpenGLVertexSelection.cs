using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes controlled vertex-array selection for backend draws and existing exact restoration scopes.</summary>
internal static class OpenGLVertexSelection
{
    #region Internal API
    /// <summary>Preserves known-equal suppression and EBO association knowledge while committing only original-context native return.</summary>
    internal static void Bind(StateCache cache,int vertexArray)
    {
        if(cache.VertexArrayKnowledge==vertexArray)return;
        var origin=GpuResource.CaptureCreationContext(GpuResourceKind.VertexArray);
        cache.SetVertexArrayKnowledge(null);
        GL.BindVertexArray(vertexArray);
        if(!GpuResource.IsCurrentResourceContext(origin))throw new InvalidOperationException("Vertex selection returned in a foreign context.");
        cache.SetVertexArrayKnowledge(vertexArray);
    }
    #endregion
}
