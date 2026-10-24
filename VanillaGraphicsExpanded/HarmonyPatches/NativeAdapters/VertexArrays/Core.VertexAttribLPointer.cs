using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexAttribLPointer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer(int @index, int @size, VertexAttribDoubleType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer(int @index, int @size, VertexAttribDPointerType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer(uint @index, int @size, VertexAttribDoubleType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer(uint @index, int @size, VertexAttribDPointerType @type, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDoubleType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDoubleType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDoubleType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDoubleType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDoubleType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDoubleType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDoubleType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDoubleType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);
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
