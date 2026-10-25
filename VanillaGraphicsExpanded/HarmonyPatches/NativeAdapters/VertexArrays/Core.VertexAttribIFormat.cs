using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexAttribIFormat overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribIFormat(int @attribindex, int @size, VertexAttribIntegerType @type, int @relativeoffset) => StateCache.Current.ExecuteCoreVertexAttribIFormat(@attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribIFormat(uint @attribindex, int @size, VertexAttribIntegerType @type, uint @relativeoffset) => StateCache.Current.ExecuteCoreVertexAttribIFormat(@attribindex, @size, @type, @relativeoffset);

    #endregion
}
