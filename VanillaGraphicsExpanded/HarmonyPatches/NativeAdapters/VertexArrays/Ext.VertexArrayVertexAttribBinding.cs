using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribBinding overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribBinding(int @vaobj, int @attribindex, int @bindingindex) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribBinding(@vaobj, @attribindex, @bindingindex);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribBinding(uint @vaobj, uint @attribindex, uint @bindingindex) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribBinding(@vaobj, @attribindex, @bindingindex);

    #endregion
}
