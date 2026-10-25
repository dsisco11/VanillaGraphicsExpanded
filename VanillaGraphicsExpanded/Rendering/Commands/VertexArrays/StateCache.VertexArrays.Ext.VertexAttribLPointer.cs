using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext VertexAttribLPointer dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer(int @index, int @size, VertexAttribLType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer(int @index, int @size, VertexAttribPointerType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer(uint @index, int @size, VertexAttribLType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer(uint @index, int @size, VertexAttribPointerType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(int @index, int @size, VertexAttribLType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(int @index, int @size, VertexAttribLType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(int @index, int @size, VertexAttribLType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(int @index, int @size, VertexAttribLType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(uint @index, int @size, VertexAttribLType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(uint @index, int @size, VertexAttribLType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(uint @index, int @size, VertexAttribLType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(uint @index, int @size, VertexAttribLType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexAttribLPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
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
