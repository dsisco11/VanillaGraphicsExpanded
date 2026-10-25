using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext NamedBufferStorageExternal execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageExternal(int @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageExternal(int @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageExternal(int @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageExternal(int @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageExternal(uint @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageExternal(uint @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageExternal(uint @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageExternal(uint @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }
    #endregion
}
