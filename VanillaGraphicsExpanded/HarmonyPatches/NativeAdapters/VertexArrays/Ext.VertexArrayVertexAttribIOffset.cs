using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribIOffset overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribIOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribEnum @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribIOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribType @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribIOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribEnum @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribIOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribType @type, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);

    #endregion
}
