using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext VertexArrayVertexAttribOffset dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexArrayVertexAttribOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribOffset(@vaobj, @buffer, @index, @size, @type, @normalized, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexArrayVertexAttribOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribOffset(@vaobj, @buffer, @index, @size, @type, @normalized, @stride, @offset);
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
