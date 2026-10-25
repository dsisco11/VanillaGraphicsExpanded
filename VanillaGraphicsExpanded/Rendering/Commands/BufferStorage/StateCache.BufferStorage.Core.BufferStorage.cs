using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core BufferStorage execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage(BufferTarget @target, int @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage(BufferTarget @target, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage<T2>(BufferTarget @target, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage<T2>(BufferTarget @target, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage<T2>(BufferTarget @target, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage<T2>(BufferTarget @target, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage<T2>(BufferTarget @target, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreBufferStorage<T2>(BufferTarget @target, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.BufferStorage<T2>(@target, @size, @data, @flags); }
        finally { observation.Complete(); }
    }
    #endregion
}
