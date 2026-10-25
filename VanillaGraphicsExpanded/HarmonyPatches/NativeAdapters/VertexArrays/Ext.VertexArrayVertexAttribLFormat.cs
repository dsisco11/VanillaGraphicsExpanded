using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribLFormat overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayVertexAttribLFormat(int @vaobj, int @attribindex, int @size, ExtDirectStateAccess @type, int @relativeoffset) => StateCache.Current.ExecuteExtVertexArrayVertexAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayVertexAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribLType @type, int @relativeoffset) => StateCache.Current.ExecuteExtVertexArrayVertexAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayVertexAttribLFormat(uint @vaobj, uint @attribindex, int @size, ExtDirectStateAccess @type, uint @relativeoffset) => StateCache.Current.ExecuteExtVertexArrayVertexAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayVertexAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribLType @type, uint @relativeoffset) => StateCache.Current.ExecuteExtVertexArrayVertexAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    #endregion
}
