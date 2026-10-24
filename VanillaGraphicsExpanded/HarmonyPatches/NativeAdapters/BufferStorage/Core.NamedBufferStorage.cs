using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.NamedBufferStorage overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage(int @buffer, int @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage(int @buffer, int @size, IntPtr @data, MapBufferUsageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, MapBufferUsageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage(uint @buffer, int @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage(uint @buffer, int @size, IntPtr @data, MapBufferUsageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, MapBufferUsageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.NamedBufferStorage<T2>(@buffer, @size, @data, @flags);
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
