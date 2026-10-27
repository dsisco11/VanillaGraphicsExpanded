using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core EnableVertexAttribArray dispatch and its vertex revision publication.</summary>
internal static unsafe partial class OpenGLVertexCalls
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreEnableVertexAttribArray(int @index)
    {
        var observation = OpenGlStateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.EnableVertexAttribArray(@index);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreEnableVertexAttribArray(uint @index)
    {
        var observation = OpenGlStateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.EnableVertexAttribArray(@index);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    #endregion
}
