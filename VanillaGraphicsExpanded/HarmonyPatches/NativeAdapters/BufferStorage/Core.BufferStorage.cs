using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.BufferStorage overloads through their shared metadata observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage(BufferTarget @target, int @size, IntPtr @data, BufferStorageFlags @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage(@target, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage(BufferTarget @target, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage(@target, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage<T2>(@target, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage<T2>(@target, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage<T2>(@target, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage<T2>(@target, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage<T2>(@target, @size, ref @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage<T2>(@target, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage<T2>(@target, @size, @data, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteCoreBufferStorage<T2>(@target, @size, @data, @flags);

    #endregion
}
