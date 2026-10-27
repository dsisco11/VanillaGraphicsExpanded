using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext VertexArrayVertexAttribIOffset dispatch and its vertex revision publication.</summary>
internal static unsafe partial class OpenGLVertexCalls
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteExtVertexArrayVertexAttribIOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribEnum @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteExtVertexArrayVertexAttribIOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribType @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteExtVertexArrayVertexAttribIOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribEnum @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteExtVertexArrayVertexAttribIOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribType @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
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
