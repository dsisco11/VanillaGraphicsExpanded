using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexArrayAttribIFormat overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayAttribIFormat(int @vaobj, int @attribindex, int @size, VertexAttribIntegerType @type, int @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexArrayAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayAttribIFormat(int @vaobj, int @attribindex, int @size, VertexAttribIType @type, int @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexArrayAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayAttribIFormat(int @vaobj, int @attribindex, int @size, VertexAttribType @type, int @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexArrayAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayAttribIFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribIntegerType @type, uint @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexArrayAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayAttribIFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribIType @type, uint @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexArrayAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayAttribIFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribType @type, uint @relativeoffset) => OpenGLVertexCalls.ExecuteCoreVertexArrayAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    #endregion
}
