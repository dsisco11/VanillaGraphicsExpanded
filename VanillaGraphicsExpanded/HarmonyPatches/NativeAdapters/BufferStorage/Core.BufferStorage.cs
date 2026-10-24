using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.BufferStorage overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage(BufferTarget @target, int @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage(@target, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage(BufferTarget @target, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage(@target, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage<T2>(@target, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage<T2>(@target, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage<T2>(@target, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage<T2>(@target, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage<T2>(@target, @size, ref @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage<T2>(@target, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage<T2>(@target, @size, @data, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferStorage<T2>(@target, @size, @data, @flags);
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
