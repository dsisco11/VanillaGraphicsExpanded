using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core VertexAttribIPointer dispatch and its vertex revision publication.</summary>
internal static unsafe partial class OpenGLVertexCalls
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer(int @index, int @size, VertexAttribIntegerType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer(int @index, int @size, VertexAttribIPointerType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer(uint @index, int @size, VertexAttribIntegerType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer(uint @index, int @size, VertexAttribIPointerType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(int @index, int @size, VertexAttribIntegerType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(int @index, int @size, VertexAttribIntegerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(int @index, int @size, VertexAttribIntegerType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(int @index, int @size, VertexAttribIntegerType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(int @index, int @size, VertexAttribIPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(int @index, int @size, VertexAttribIPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(int @index, int @size, VertexAttribIPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(int @index, int @size, VertexAttribIPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIntegerType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIntegerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIntegerType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIntegerType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal static void ExecuteCoreVertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);
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
