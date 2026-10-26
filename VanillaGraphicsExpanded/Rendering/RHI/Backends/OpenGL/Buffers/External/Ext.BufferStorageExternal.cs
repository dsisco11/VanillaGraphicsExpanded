using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext BufferStorageExternal execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLBufferStorageCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtBufferStorageExternal(ExtExternalBuffer @target, IntPtr @offset, int @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Ext.BufferStorageExternal(@target, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtBufferStorageExternal(ExtExternalBuffer @target, IntPtr @offset, int @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Ext.BufferStorageExternal(@target, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtBufferStorageExternal(ExtExternalBuffer @target, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Ext.BufferStorageExternal(@target, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtBufferStorageExternal(ExtExternalBuffer @target, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Ext.BufferStorageExternal(@target, @offset, @size, @clientBuffer, @flags); }
        finally { observation.Complete(); }
    }
    #endregion
}
