using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Ext.NamedBufferStorageMem overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorageMem(int @buffer, int @size, int @memory, long @offset)
        => StateCache.Current.ExecuteExtNamedBufferStorageMem(@buffer, @size, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorageMem(int @buffer, IntPtr @size, int @memory, long @offset)
        => StateCache.Current.ExecuteExtNamedBufferStorageMem(@buffer, @size, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorageMem(uint @buffer, int @size, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtNamedBufferStorageMem(@buffer, @size, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedBufferStorageMem(uint @buffer, IntPtr @size, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtNamedBufferStorageMem(@buffer, @size, @memory, @offset);

    #endregion
}
