using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Ext.NamedBufferData overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData(int @buffer, int @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData(int @buffer, int @size, IntPtr @data, ExtDirectStateAccess @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData(int @buffer, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData(int @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData(uint @buffer, int @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData(uint @buffer, int @size, IntPtr @data, ExtDirectStateAccess @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData(uint @buffer, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData(uint @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @usage)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage);
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
