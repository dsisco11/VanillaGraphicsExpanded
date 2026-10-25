using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Arb.BufferData overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData(BufferTargetArb @target, int @size, IntPtr @data, BufferUsageArb @usage)
        => StateCache.Current.ExecuteArbBufferData(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData(BufferTargetArb @target, IntPtr @size, IntPtr @data, BufferUsageArb @usage)
        => StateCache.Current.ExecuteArbBufferData(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, ref T2 @data, BufferUsageArb @usage)
        where T2 : struct
        => StateCache.Current.ExecuteArbBufferData<T2>(@target, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, T2[,,] @data, BufferUsageArb @usage)
        where T2 : struct
        => StateCache.Current.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, T2[,] @data, BufferUsageArb @usage)
        where T2 : struct
        => StateCache.Current.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, T2[] @data, BufferUsageArb @usage)
        where T2 : struct
        => StateCache.Current.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, ref T2 @data, BufferUsageArb @usage)
        where T2 : struct
        => StateCache.Current.ExecuteArbBufferData<T2>(@target, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[,,] @data, BufferUsageArb @usage)
        where T2 : struct
        => StateCache.Current.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[,] @data, BufferUsageArb @usage)
        where T2 : struct
        => StateCache.Current.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[] @data, BufferUsageArb @usage)
        where T2 : struct
        => StateCache.Current.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    #endregion
}
