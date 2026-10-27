using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core BindVertexBuffer dispatch and its vertex revision publication.</summary>
internal static unsafe partial class OpenGLVertexCalls
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreBindVertexBuffer(int @bindingindex, int @buffer, IntPtr @offset, int @stride)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.BindVertexBuffer(@bindingindex, @buffer, @offset, @stride);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreBindVertexBuffer(uint @bindingindex, uint @buffer, IntPtr @offset, int @stride)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.BindVertexBuffer(@bindingindex, @buffer, @offset, @stride);
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
