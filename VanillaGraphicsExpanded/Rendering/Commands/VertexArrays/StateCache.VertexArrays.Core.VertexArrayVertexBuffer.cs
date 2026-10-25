using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core VertexArrayVertexBuffer dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexArrayVertexBuffer(int @vaobj, int @bindingindex, int @buffer, IntPtr @offset, int @stride)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayVertexBuffer(@vaobj, @bindingindex, @buffer, @offset, @stride);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexArrayVertexBuffer(uint @vaobj, uint @bindingindex, uint @buffer, IntPtr @offset, int @stride)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayVertexBuffer(@vaobj, @bindingindex, @buffer, @offset, @stride);
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
