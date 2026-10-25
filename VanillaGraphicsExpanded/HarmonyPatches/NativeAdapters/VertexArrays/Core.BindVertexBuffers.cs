using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.BindVertexBuffers overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindVertexBuffers(int @first, int @count, ref int @buffers, ref IntPtr @offsets, ref int @strides) => StateCache.Current.ExecuteCoreBindVertexBuffers(@first, @count, ref @buffers, ref @offsets, ref @strides);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindVertexBuffers(int @first, int @count, int[] @buffers, IntPtr[] @offsets, int[] @strides) => StateCache.Current.ExecuteCoreBindVertexBuffers(@first, @count, @buffers, @offsets, @strides);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindVertexBuffers(int @first, int @count, int* @buffers, IntPtr* @offsets, int* @strides) => StateCache.Current.ExecuteCoreBindVertexBuffers(@first, @count, @buffers, @offsets, @strides);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindVertexBuffers(uint @first, int @count, ref uint @buffers, ref IntPtr @offsets, ref int @strides) => StateCache.Current.ExecuteCoreBindVertexBuffers(@first, @count, ref @buffers, ref @offsets, ref @strides);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindVertexBuffers(uint @first, int @count, uint[] @buffers, IntPtr[] @offsets, int[] @strides) => StateCache.Current.ExecuteCoreBindVertexBuffers(@first, @count, @buffers, @offsets, @strides);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindVertexBuffers(uint @first, int @count, uint* @buffers, IntPtr* @offsets, int* @strides) => StateCache.Current.ExecuteCoreBindVertexBuffers(@first, @count, @buffers, @offsets, @strides);

    #endregion
}
