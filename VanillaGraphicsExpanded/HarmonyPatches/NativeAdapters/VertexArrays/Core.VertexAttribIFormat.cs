using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexAttribIFormat overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIFormat(int @attribindex, int @size, VertexAttribIntegerType @type, int @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexAttribIFormat(@attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribIFormat(uint @attribindex, int @size, VertexAttribIntegerType @type, uint @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexAttribIFormat(@attribindex, @size, @type, @relativeoffset);

    #endregion
}
