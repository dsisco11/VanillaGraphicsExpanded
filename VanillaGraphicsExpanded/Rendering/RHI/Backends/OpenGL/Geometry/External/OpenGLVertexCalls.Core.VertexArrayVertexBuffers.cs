using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core VertexArrayVertexBuffers dispatch and its vertex revision publication.</summary>
internal static unsafe partial class OpenGLVertexCalls
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexArrayVertexBuffers(int @vaobj, int @first, int @count, ref int @buffers, ref IntPtr @offsets, ref int @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayVertexBuffers(@vaobj, @first, @count, ref @buffers, ref @offsets, ref @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexArrayVertexBuffers(int @vaobj, int @first, int @count, int[] @buffers, IntPtr[] @offsets, int[] @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayVertexBuffers(@vaobj, @first, @count, @buffers, @offsets, @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexArrayVertexBuffers(int @vaobj, int @first, int @count, int* @buffers, IntPtr* @offsets, int* @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayVertexBuffers(@vaobj, @first, @count, @buffers, @offsets, @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexArrayVertexBuffers(uint @vaobj, uint @first, int @count, ref uint @buffers, ref IntPtr @offsets, ref int @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayVertexBuffers(@vaobj, @first, @count, ref @buffers, ref @offsets, ref @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexArrayVertexBuffers(uint @vaobj, uint @first, int @count, uint[] @buffers, IntPtr[] @offsets, int[] @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayVertexBuffers(@vaobj, @first, @count, @buffers, @offsets, @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexArrayVertexBuffers(uint @vaobj, uint @first, int @count, uint* @buffers, IntPtr* @offsets, int* @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayVertexBuffers(@vaobj, @first, @count, @buffers, @offsets, @strides);
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
