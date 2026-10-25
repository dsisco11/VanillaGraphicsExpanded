using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.BufferData overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData(BufferTarget @target, int @size, IntPtr @data, BufferUsageHint @usage)
        => StateCache.Current.ExecuteCoreBufferData(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData(BufferTarget @target, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
        => StateCache.Current.ExecuteCoreBufferData(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTarget @target, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteCoreBufferData<T2>(@target, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTarget @target, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteCoreBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTarget @target, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteCoreBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTarget @target, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteCoreBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTarget @target, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteCoreBufferData<T2>(@target, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTarget @target, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteCoreBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTarget @target, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteCoreBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTarget @target, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
        => StateCache.Current.ExecuteCoreBufferData<T2>(@target, @size, @data, @usage);

    #endregion
}
