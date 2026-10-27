using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexAttribIPointer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribIPointer(int @index, int @size, VertexAttribIType @type, int @stride, IntPtr @pointer) => StateCache.Current.ExecuteExtVertexAttribIPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribIPointer(int @index, int @size, VertexAttribPointerType @type, int @stride, IntPtr @pointer) => StateCache.Current.ExecuteExtVertexAttribIPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribIPointer(uint @index, int @size, VertexAttribIType @type, int @stride, IntPtr @pointer) => StateCache.Current.ExecuteExtVertexAttribIPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribIPointer(uint @index, int @size, VertexAttribPointerType @type, int @stride, IntPtr @pointer) => StateCache.Current.ExecuteExtVertexAttribIPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIType @type, int @stride, ref T4 @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIType @type, int @stride, T4[,] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIType @type, int @stride, T4[] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIType @type, int @stride, ref T4 @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIType @type, int @stride, T4[,] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIType @type, int @stride, T4[] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct => StateCache.Current.ExecuteExtVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    #endregion
}
