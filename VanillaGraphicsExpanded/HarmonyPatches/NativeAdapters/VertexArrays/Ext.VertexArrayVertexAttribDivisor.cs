using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribDivisor overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribDivisor(int @vaobj, int @index, int @divisor) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribDivisor(@vaobj, @index, @divisor);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribDivisor(uint @vaobj, uint @index, uint @divisor) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribDivisor(@vaobj, @index, @divisor);

    #endregion
}
