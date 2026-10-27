using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribFormat overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribFormat(int @vaobj, int @attribindex, int @size, ExtDirectStateAccess @type, bool @normalized, int @relativeoffset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribFormat(@vaobj, @attribindex, @size, @type, @normalized, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribFormat(int @vaobj, int @attribindex, int @size, VertexAttribType @type, bool @normalized, int @relativeoffset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribFormat(@vaobj, @attribindex, @size, @type, @normalized, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribFormat(uint @vaobj, uint @attribindex, int @size, ExtDirectStateAccess @type, bool @normalized, uint @relativeoffset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribFormat(@vaobj, @attribindex, @size, @type, @normalized, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayVertexAttribFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribType @type, bool @normalized, uint @relativeoffset) => OpenGLVertexCalls.ExecuteExtVertexArrayVertexAttribFormat(@vaobj, @attribindex, @size, @type, @normalized, @relativeoffset);

    #endregion
}
