using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core BindVertexBuffers dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreBindVertexBuffers(int @first, int @count, ref int @buffers, ref IntPtr @offsets, ref int @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.BindVertexBuffers(@first, @count, ref @buffers, ref @offsets, ref @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreBindVertexBuffers(int @first, int @count, int[] @buffers, IntPtr[] @offsets, int[] @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.BindVertexBuffers(@first, @count, @buffers, @offsets, @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreBindVertexBuffers(int @first, int @count, int* @buffers, IntPtr* @offsets, int* @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.BindVertexBuffers(@first, @count, @buffers, @offsets, @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreBindVertexBuffers(uint @first, int @count, ref uint @buffers, ref IntPtr @offsets, ref int @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.BindVertexBuffers(@first, @count, ref @buffers, ref @offsets, ref @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreBindVertexBuffers(uint @first, int @count, uint[] @buffers, IntPtr[] @offsets, int[] @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.BindVertexBuffers(@first, @count, @buffers, @offsets, @strides);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreBindVertexBuffers(uint @first, int @count, uint* @buffers, IntPtr* @offsets, int* @strides)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.BindVertexBuffers(@first, @count, @buffers, @offsets, @strides);
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
