using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core NamedBufferData execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData(int @buffer, int @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData(int @buffer, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData(uint @buffer, int @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData(uint @buffer, IntPtr @size, IntPtr @data, BufferUsageHint @usage)
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(int @buffer, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(int @buffer, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(int @buffer, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(int @buffer, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(int @buffer, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(int @buffer, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(int @buffer, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(int @buffer, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(uint @buffer, int @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(uint @buffer, int @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(uint @buffer, int @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(uint @buffer, int @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(uint @buffer, IntPtr @size, ref T2 @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, ref @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(uint @buffer, IntPtr @size, T2[,] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedBufferData<T2>(uint @buffer, IntPtr @size, T2[] @data, BufferUsageHint @usage)
        where T2 : struct
    {
        var observation = StateCache.InvalidateNativeBufferStorage(unchecked((int)@buffer), true);
        try { GL.NamedBufferData<T2>(@buffer, @size, @data, @usage); }
        finally { observation.Complete(); }
    }
    #endregion
}
