using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core VertexArrayElementBuffer dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLVertexCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreVertexArrayElementBuffer(int @vaobj, int @buffer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, true);
        bool returned = false;
        try
        {
            GL.VertexArrayElementBuffer(@vaobj, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreVertexArrayElementBuffer(uint @vaobj, uint @buffer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, true);
        bool returned = false;
        try
        {
            GL.VertexArrayElementBuffer(@vaobj, @buffer);
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
