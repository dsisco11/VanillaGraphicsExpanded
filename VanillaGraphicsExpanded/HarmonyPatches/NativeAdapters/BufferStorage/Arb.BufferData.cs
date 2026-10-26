using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Arb.BufferData overloads through their shared metadata observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData(BufferTargetArb @target, int @size, IntPtr @data, BufferUsageArb @usage)
        => OpenGLBufferStorageCalls.ExecuteArbBufferData(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData(BufferTargetArb @target, IntPtr @size, IntPtr @data, BufferUsageArb @usage)
        => OpenGLBufferStorageCalls.ExecuteArbBufferData(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, ref T2 @data, BufferUsageArb @usage)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteArbBufferData<T2>(@target, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, T2[,,] @data, BufferUsageArb @usage)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, T2[,] @data, BufferUsageArb @usage)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, int @size, T2[] @data, BufferUsageArb @usage)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, ref T2 @data, BufferUsageArb @usage)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteArbBufferData<T2>(@target, @size, ref @data, @usage);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[,,] @data, BufferUsageArb @usage)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[,] @data, BufferUsageArb @usage)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[] @data, BufferUsageArb @usage)
        where T2 : struct
        => OpenGLBufferStorageCalls.ExecuteArbBufferData<T2>(@target, @size, @data, @usage);

    #endregion
}
