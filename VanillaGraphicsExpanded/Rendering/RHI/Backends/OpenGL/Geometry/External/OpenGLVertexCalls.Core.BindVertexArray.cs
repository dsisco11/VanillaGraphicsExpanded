using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core BindVertexArray dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLVertexCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreBindVertexArray(int @array)
    {
        var observation = OpenGlStateCache.BeginNativeVertexSelection(unchecked((int)@array));
        bool returned = false;
        try
        {
            GL.BindVertexArray(@array);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreBindVertexArray(uint @array)
    {
        var observation = OpenGlStateCache.BeginNativeVertexSelection(unchecked((int)@array));
        bool returned = false;
        try
        {
            GL.BindVertexArray(@array);
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
