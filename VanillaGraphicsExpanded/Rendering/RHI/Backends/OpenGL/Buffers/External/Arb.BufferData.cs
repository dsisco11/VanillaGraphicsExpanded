using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Arb BufferData execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLBufferStorageCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData(BufferTargetArb @target, int @size, IntPtr @data, BufferUsageArb @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData(BufferTargetArb @target, IntPtr @size, IntPtr @data, BufferUsageArb @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData<T2>(BufferTargetArb @target, int @size, ref T2 @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData<T2>(@target, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData<T2>(BufferTargetArb @target, int @size, T2[,,] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData<T2>(BufferTargetArb @target, int @size, T2[,] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData<T2>(BufferTargetArb @target, int @size, T2[] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData<T2>(BufferTargetArb @target, IntPtr @size, ref T2 @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData<T2>(@target, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[,,] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[,] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteArbBufferData<T2>(BufferTargetArb @target, IntPtr @size, T2[] @data, BufferUsageArb @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Arb.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }
    #endregion
}
