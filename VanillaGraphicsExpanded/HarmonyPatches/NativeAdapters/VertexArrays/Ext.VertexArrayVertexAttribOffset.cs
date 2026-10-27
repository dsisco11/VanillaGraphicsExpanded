using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribOffset overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribOffset(@vaobj, @buffer, @index, @size, @type, @normalized, @stride, @offset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribPointerType @type, bool @normalized, int @stride, IntPtr @offset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribOffset(@vaobj, @buffer, @index, @size, @type, @normalized, @stride, @offset);

    #endregion
}
