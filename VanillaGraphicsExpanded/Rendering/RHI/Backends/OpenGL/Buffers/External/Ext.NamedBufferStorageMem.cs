using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext NamedBufferStorageMem execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLBufferStorageCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageMem(int @buffer, int @size, int @memory, long @offset)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageMem(int @buffer, IntPtr @size, int @memory, long @offset)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageMem(uint @buffer, int @size, uint @memory, ulong @offset)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedBufferStorageMem(uint @buffer, IntPtr @size, uint @memory, ulong @offset)
    {
        var observation = OpenGlStateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }
    #endregion
}
