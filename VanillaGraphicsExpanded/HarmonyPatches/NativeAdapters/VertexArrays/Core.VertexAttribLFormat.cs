using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexAttribLFormat overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLFormat(int @attribindex, int @size, VertexAttribDoubleType @type, int @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexAttribLFormat(@attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribLFormat(uint @attribindex, int @size, VertexAttribDoubleType @type, uint @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexAttribLFormat(@attribindex, @size, @type, @relativeoffset);

    #endregion
}
