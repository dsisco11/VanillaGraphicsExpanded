using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Ext.NamedBufferData overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData(int @buffer, int @size, IntPtr @data, BufferUsageHint @usage)
        => StateCache.Current.ExecuteExtNamedBufferData(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData(int @buffer, int @size, IntPtr @data, ExtDirectStateAccess @usage)
        => StateCache.Current.ExecuteExtNamedBufferData(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData(int @buffer, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
        => StateCache.Current.ExecuteExtNamedBufferData(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData(int @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @usage)
        => StateCache.Current.ExecuteExtNamedBufferData(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData(uint @buffer, int @size, IntPtr @data, BufferUsageHint @usage)
        => StateCache.Current.ExecuteExtNamedBufferData(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData(uint @buffer, int @size, IntPtr @data, ExtDirectStateAccess @usage)
        => StateCache.Current.ExecuteExtNamedBufferData(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData(uint @buffer, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
        => StateCache.Current.ExecuteExtNamedBufferData(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData(uint @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @usage)
        => StateCache.Current.ExecuteExtNamedBufferData(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, int @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(int @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, int @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferData<T2>(uint @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferData<T2>(@buffer, @size, @data, @usage);

    #endregion
}
