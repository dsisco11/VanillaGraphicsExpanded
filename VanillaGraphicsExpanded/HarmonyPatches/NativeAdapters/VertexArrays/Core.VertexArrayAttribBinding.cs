using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexArrayAttribBinding overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayAttribBinding(int @vaobj, int @attribindex, int @bindingindex) => OpenGLVertexCalls.ExecuteCoreVertexArrayAttribBinding(@vaobj, @attribindex, @bindingindex);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayAttribBinding(uint @vaobj, uint @attribindex, uint @bindingindex) => OpenGLVertexCalls.ExecuteCoreVertexArrayAttribBinding(@vaobj, @attribindex, @bindingindex);

    #endregion
}
