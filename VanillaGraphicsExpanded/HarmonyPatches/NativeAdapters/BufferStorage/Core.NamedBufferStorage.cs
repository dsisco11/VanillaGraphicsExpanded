using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.NamedBufferStorage overloads through their shared metadata observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, int @size, IntPtr @data, BufferStorageFlags @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, int @size, IntPtr @data, MapBufferUsageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, MapBufferUsageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, int @size, IntPtr @data, BufferStorageFlags @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, int @size, IntPtr @data, MapBufferUsageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, MapBufferUsageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreNamedBufferStorage<T2>(@buffer, @size, @data, @flags);

    #endregion
}
