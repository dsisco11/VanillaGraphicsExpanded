using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core VertexAttribPointer dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer(int @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, int @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer(@index, @size, @type, @normalized, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer(int @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer(uint @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, ref T5 @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, T5[,,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, T5[,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, T5[] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, ref T5 @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, T5[,,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, T5[,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, T5[] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
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
