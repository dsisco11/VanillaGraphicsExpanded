using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext NamedBufferStorageMem execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageMem(int @buffer, int @size, int @memory, long @offset)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageMem(int @buffer, IntPtr @size, int @memory, long @offset)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageMem(uint @buffer, int @size, uint @memory, ulong @offset)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedBufferStorageMem(uint @buffer, IntPtr @size, uint @memory, ulong @offset)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }
    #endregion
}
