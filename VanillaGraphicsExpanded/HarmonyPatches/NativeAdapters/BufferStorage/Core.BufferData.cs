using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.BufferData overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData(BufferTarget @target, int @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData(BufferTarget @target, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTarget @target, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData<T2>(@target, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTarget @target, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTarget @target, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTarget @target, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTarget @target, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData<T2>(@target, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTarget @target, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTarget @target, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTarget @target, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.BufferData<T2>(@target, @size, @data, @usage);
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
