using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.BindBuffer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGlStateCache execution boundary.</summary>
    internal static void BindBuffer(BufferTarget @target, int @buffer) => OpenGLBufferBindingCalls.ExecuteCoreBindBuffer(@target, @buffer);

    /// <summary>Forwards the exact signature to the OpenGlStateCache execution boundary.</summary>
    internal static void BindBuffer(BufferTarget @target, uint @buffer) => OpenGLBufferBindingCalls.ExecuteCoreBindBuffer(@target, @buffer);

    #endregion
}
