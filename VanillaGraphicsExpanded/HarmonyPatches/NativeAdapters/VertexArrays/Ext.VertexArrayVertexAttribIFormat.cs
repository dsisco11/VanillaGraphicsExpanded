using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribIFormat overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayVertexAttribIFormat(int @vaobj, int @attribindex, int @size, ExtDirectStateAccess @type, int @relativeoffset) => StateCache.Current.ExecuteExtVertexArrayVertexAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayVertexAttribIFormat(int @vaobj, int @attribindex, int @size, VertexAttribIType @type, int @relativeoffset) => StateCache.Current.ExecuteExtVertexArrayVertexAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayVertexAttribIFormat(uint @vaobj, uint @attribindex, int @size, ExtDirectStateAccess @type, uint @relativeoffset) => StateCache.Current.ExecuteExtVertexArrayVertexAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayVertexAttribIFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribIType @type, uint @relativeoffset) => StateCache.Current.ExecuteExtVertexArrayVertexAttribIFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    #endregion
}
