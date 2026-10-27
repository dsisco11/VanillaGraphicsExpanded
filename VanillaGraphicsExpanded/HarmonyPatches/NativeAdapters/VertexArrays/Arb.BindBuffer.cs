using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Arb.BindBuffer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGlStateCache execution boundary.</summary>
    internal static void BindBuffer(BufferTargetArb @target, int @buffer) => OpenGLBufferBindingCalls.ExecuteArbBindBuffer(@target, @buffer);

    /// <summary>Forwards the exact signature to the OpenGlStateCache execution boundary.</summary>
    internal static void BindBuffer(BufferTargetArb @target, uint @buffer) => OpenGLBufferBindingCalls.ExecuteArbBindBuffer(@target, @buffer);

    #endregion
}
