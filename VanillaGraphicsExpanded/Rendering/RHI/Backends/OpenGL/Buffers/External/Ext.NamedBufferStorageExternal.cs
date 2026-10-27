using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext NamedBufferStorageExternal execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLBufferStorageCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageExternal(int @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageExternal(int @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageExternal(int @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageExternal(int @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageExternal(uint @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageExternal(uint @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageExternal(uint @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageExternal(uint @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }
    #endregion
}
