using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexArrayBindingDivisor overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayBindingDivisor(int @vaobj, int @bindingindex, int @divisor) => OpenGLVertexCalls.ExecuteCoreVertexArrayBindingDivisor(@vaobj, @bindingindex, @divisor);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayBindingDivisor(uint @vaobj, uint @bindingindex, uint @divisor) => OpenGLVertexCalls.ExecuteCoreVertexArrayBindingDivisor(@vaobj, @bindingindex, @divisor);

    #endregion
}
