using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.BindVertexBuffer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void BindVertexBuffer(int @bindingindex, int @buffer, IntPtr @offset, int @stride) => OpenGLVertexCalls.ExecuteCoreBindVertexBuffer(@bindingindex, @buffer, @offset, @stride);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void BindVertexBuffer(uint @bindingindex, uint @buffer, IntPtr @offset, int @stride) => OpenGLVertexCalls.ExecuteCoreBindVertexBuffer(@bindingindex, @buffer, @offset, @stride);

    #endregion
}
