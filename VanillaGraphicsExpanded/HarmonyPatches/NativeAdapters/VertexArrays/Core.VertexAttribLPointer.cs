using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexAttribLPointer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer(int @index, int @size, VertexAttribDoubleType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer(int @index, int @size, VertexAttribDPointerType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer(uint @index, int @size, VertexAttribDoubleType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer(uint @index, int @size, VertexAttribDPointerType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDoubleType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDoubleType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDoubleType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDoubleType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(int @index, int @size, VertexAttribDPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDoubleType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDoubleType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDoubleType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDoubleType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLPointer<T4>(uint @index, int @size, VertexAttribDPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribLPointer<T4>(@index, @size, @type, @stride, @pointer);

    #endregion
}
