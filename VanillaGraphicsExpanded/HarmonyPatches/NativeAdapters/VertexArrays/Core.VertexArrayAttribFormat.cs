using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexArrayAttribFormat overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayAttribFormat(int @vaobj, int @attribindex, int @size, VertexAttribType @type, bool @normalized, int @relativeoffset) => StateCache.Current.ExecuteCoreVertexArrayAttribFormat(@vaobj, @attribindex, @size, @type, @normalized, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayAttribFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribType @type, bool @normalized, uint @relativeoffset) => StateCache.Current.ExecuteCoreVertexArrayAttribFormat(@vaobj, @attribindex, @size, @type, @normalized, @relativeoffset);

    #endregion
}
