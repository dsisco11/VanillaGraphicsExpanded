using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext BufferStorageMem execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtBufferStorageMem(BufferTargetArb @target, int @size, int @memory, long @offset)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Ext.BufferStorageMem(@target, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtBufferStorageMem(BufferTargetArb @target, int @size, uint @memory, ulong @offset)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Ext.BufferStorageMem(@target, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtBufferStorageMem(BufferTargetArb @target, IntPtr @size, int @memory, long @offset)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Ext.BufferStorageMem(@target, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtBufferStorageMem(BufferTargetArb @target, IntPtr @size, uint @memory, ulong @offset)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@target), false);
        try { GL.Ext.BufferStorageMem(@target, @size, @memory, @offset); }
        finally { observation.Complete(); }
    }
    #endregion
}
