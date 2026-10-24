using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Arb.BufferData overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData(BufferTargetArb @target, int @size, IntPtr @data, BufferUsageArb @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData(BufferTargetArb @target, IntPtr @size, IntPtr @data, BufferUsageArb @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, ref T2 @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData<T2>(@target, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, T2[,,] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, T2[,] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, T2[] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, ref T2 @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData<T2>(@target, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[,,] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[,] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData<T2>(@target, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Arb.BufferData<T2>(@target, @size, @data, @usage);
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
