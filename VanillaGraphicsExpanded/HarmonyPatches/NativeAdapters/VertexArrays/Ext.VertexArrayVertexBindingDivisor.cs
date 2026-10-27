using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexBindingDivisor overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexBindingDivisor(int @vaobj, int @bindingindex, int @divisor) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexBindingDivisor(@vaobj, @bindingindex, @divisor);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexBindingDivisor(uint @vaobj, uint @bindingindex, uint @divisor) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexBindingDivisor(@vaobj, @bindingindex, @divisor);

    #endregion
}
