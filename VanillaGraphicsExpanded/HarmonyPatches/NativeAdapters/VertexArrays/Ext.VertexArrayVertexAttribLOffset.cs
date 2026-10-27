using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribLOffset overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribLOffset(int @vaobj, int @buffer, int @index, int @size, ExtDirectStateAccess @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribLOffset(int @vaobj, int @buffer, int @index, int @size, ExtVertexAttrib64bit @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribLOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribLType @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribLOffset(uint @vaobj, uint @buffer, uint @index, int @size, ExtDirectStateAccess @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribLOffset(uint @vaobj, uint @buffer, uint @index, int @size, ExtVertexAttrib64bit @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribLOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribLType @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    #endregion
}
