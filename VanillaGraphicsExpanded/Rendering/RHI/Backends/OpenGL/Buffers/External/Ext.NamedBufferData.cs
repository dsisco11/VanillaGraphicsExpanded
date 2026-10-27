using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext NamedBufferData execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLBufferStorageCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData(int @buffer, int @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData(int @buffer, int @size, IntPtr @data, ExtDirectStateAccess @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData(int @buffer, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData(int @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData(uint @buffer, int @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData(uint @buffer, int @size, IntPtr @data, ExtDirectStateAccess @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData(uint @buffer, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData(uint @buffer, IntPtr @size, IntPtr @data, ExtDirectStateAccess @usage)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, int @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, int @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, int @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(int @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, int @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, int @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, int @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, int @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, IntPtr @size, ref T2 @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferData<T2>(uint @buffer, IntPtr @size, T2[] @data, ExtDirectStateAccess @usage)
        where T2 : struct
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }
    #endregion
}
