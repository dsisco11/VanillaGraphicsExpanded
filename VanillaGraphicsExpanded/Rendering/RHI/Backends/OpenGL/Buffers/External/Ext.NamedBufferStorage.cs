using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext NamedBufferStorage execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLBufferStorageCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(int @buffer, int @size, IntPtr @data, int @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(int @buffer, int @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(int @buffer, int @size, IntPtr @data, ExtDirectStateAccess @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(int @buffer, int @size, IntPtr @data, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, int @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(int @buffer, IntPtr @size, IntPtr @data, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(uint @buffer, int @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(uint @buffer, int @size, IntPtr @data, ExtDirectStateAccess @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(uint @buffer, int @size, IntPtr @data, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(uint @buffer, int @size, IntPtr @data, uint @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, BufferStorageFlags @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage(uint @buffer, IntPtr @size, IntPtr @data, uint @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, int @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, int @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, int @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, int @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, int @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, int @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, int @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, int @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, int @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(int @buffer, IntPtr @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, ref T2 @data, uint @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[,,] @data, uint @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[,] @data, uint @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, int @size, T2[] @data, uint @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, ref T2 @data, uint @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, ref @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,,] @data, uint @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[,] @data, uint @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, BufferStorageFlags @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, MapBufferUsageMask @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorage<T2>(uint @buffer, IntPtr @size, T2[] @data, uint @flags)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorage<T2>(@buffer, @size, @data, @flags); }
        finally { observation.Complete(); }
    }
    #endregion
}
