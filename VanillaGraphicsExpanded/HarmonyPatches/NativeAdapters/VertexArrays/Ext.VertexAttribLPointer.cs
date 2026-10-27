using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexAttribLPointer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer(int @index, int @size, VertexAttribLType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer(int @index, int @size, VertexAttribPointerType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer(uint @index, int @size, VertexAttribLType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer(uint @index, int @size, VertexAttribPointerType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribLType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribLType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribLType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribLType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribLType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribLType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribLType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribLType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteExtVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    #endregion
}
