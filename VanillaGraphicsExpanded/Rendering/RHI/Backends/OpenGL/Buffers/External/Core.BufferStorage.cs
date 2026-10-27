using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core BufferStorage execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLBufferStorageCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage(BufferTarget @target, int @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage(BufferTarget @target, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage<T2>(BufferTarget @target, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage<T2>(BufferTarget @target, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage<T2>(BufferTarget @target, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage<T2>(BufferTarget @target, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage<T2>(BufferTarget @target, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreBufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }
    #endregion
}
