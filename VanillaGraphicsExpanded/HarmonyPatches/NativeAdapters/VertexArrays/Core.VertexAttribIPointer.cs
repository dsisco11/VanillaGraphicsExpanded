using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexAttribIPointer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer(int @index, int @size, VertexAttribIntegerType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer(int @index, int @size, VertexAttribIPointerType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer(uint @index, int @size, VertexAttribIntegerType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer(uint @index, int @size, VertexAttribIPointerType @type, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIntegerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIntegerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIntegerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIntegerType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(int @index, int @size, VertexAttribIPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIntegerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIntegerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIntegerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIntegerType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIPointerType @type, int @stride, ref T4 @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIPointerType @type, int @stride, T4[,,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIPointerType @type, int @stride, T4[,] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIPointer<T4>(uint @index, int @size, VertexAttribIPointerType @type, int @stride, T4[] @pointer)
        where T4 : struct => OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer<T4>(@index, @size, @type, @stride, @pointer);

    #endregion
}
