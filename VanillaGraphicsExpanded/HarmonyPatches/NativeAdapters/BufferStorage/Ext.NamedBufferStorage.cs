using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Ext.NamedBufferStorage overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, int @size, IntPtr @data, int @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, int @size, IntPtr @data, BufferStorageFlags @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, int @size, IntPtr @data, ExtDirectStateAccess @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, int @size, IntPtr @data, MapBufferUsageMask @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, int @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, MapBufferUsageMask @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, int @size, IntPtr @data, BufferStorageFlags @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, int @size, IntPtr @data, ExtDirectStateAccess @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, int @size, IntPtr @data, MapBufferUsageMask @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, int @size, IntPtr @data, uint @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, MapBufferUsageMask @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, uint @flags)
        => StateCache.Current.ExecuteExtNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, int @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, int @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, int @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, int @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, int @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, int @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, int @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, int @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, uint @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, uint @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, uint @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, uint @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, uint @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, uint @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, uint @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, uint @flags)
        where T2 : struct
        => StateCache.Current.ExecuteExtNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    #endregion
}
