using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayBindVertexBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayBindVertexBuffer(int @vaobj, int @bindingindex, int @buffer, IntPtr @offset, int @stride) => OpenGLVertexCalls.ExecuteExtVertexArrayBindVertexBuffer(@vaobj, @bindingindex, @buffer, @offset, @stride);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayBindVertexBuffer(uint @vaobj, uint @bindingindex, uint @buffer, IntPtr @offset, int @stride) => OpenGLVertexCalls.ExecuteExtVertexArrayBindVertexBuffer(@vaobj, @bindingindex, @buffer, @offset, @stride);

    #endregion
}
