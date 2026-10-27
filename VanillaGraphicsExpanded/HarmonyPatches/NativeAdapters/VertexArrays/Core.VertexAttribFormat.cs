using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexAttribFormat overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribFormat(int @attribindex, int @size, VertexAttribType @type, bool @normalized, int @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexAttribFormat(@attribindex, @size, @type, @normalized, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexAttribFormat(uint @attribindex, int @size, VertexAttribType @type, bool @normalized, uint @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexAttribFormat(@attribindex, @size, @type, @normalized, @relativeoffset);

    #endregion
}
