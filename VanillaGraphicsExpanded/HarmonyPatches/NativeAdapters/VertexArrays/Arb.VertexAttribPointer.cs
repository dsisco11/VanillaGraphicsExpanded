using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Arb.VertexAttribPointer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteArbVertexAttribPointer(@index, @size, @type, @normalized, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, IntPtr @pointer) => OpenGLVertexCalls.ExecuteArbVertexAttribPointer(@index, @size, @type, @normalized, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, ref T5 @pointer)
        where T5 : struct => OpenGLVertexCalls.ExecuteArbVertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,,] @pointer)
        where T5 : struct => OpenGLVertexCalls.ExecuteArbVertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,] @pointer)
        where T5 : struct => OpenGLVertexCalls.ExecuteArbVertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[] @pointer)
        where T5 : struct => OpenGLVertexCalls.ExecuteArbVertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, ref T5 @pointer)
        where T5 : struct => OpenGLVertexCalls.ExecuteArbVertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, ref @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,,] @pointer)
        where T5 : struct => OpenGLVertexCalls.ExecuteArbVertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,] @pointer)
        where T5 : struct => OpenGLVertexCalls.ExecuteArbVertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[] @pointer)
        where T5 : struct => OpenGLVertexCalls.ExecuteArbVertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);

    #endregion
}
