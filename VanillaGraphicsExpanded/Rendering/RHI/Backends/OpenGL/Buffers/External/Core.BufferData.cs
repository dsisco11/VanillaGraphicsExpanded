using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core BufferData execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLBufferStorageCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData(BufferTarget @target, int @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData(BufferTarget @target, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData<T2>(BufferTarget @target, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData<T2>(@target, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData<T2>(BufferTarget @target, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData<T2>(BufferTarget @target, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData<T2>(BufferTarget @target, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData<T2>(BufferTarget @target, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData<T2>(@target, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData<T2>(BufferTarget @target, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData<T2>(BufferTarget @target, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferData<T2>(BufferTarget @target, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferData<T2>(@target, @size, @data, @usage); }
        finally { observation.Complete(); }
    }
    #endregion
}
