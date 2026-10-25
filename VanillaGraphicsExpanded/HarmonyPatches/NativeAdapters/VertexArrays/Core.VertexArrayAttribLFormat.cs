using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexArrayAttribLFormat overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribDoubleType @type, int @relativeoffset) => StateCache.Current.ExecuteCoreVertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribLType @type, int @relativeoffset) => StateCache.Current.ExecuteCoreVertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribType @type, int @relativeoffset) => StateCache.Current.ExecuteCoreVertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribDoubleType @type, uint @relativeoffset) => StateCache.Current.ExecuteCoreVertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribLType @type, uint @relativeoffset) => StateCache.Current.ExecuteCoreVertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribType @type, uint @relativeoffset) => StateCache.Current.ExecuteCoreVertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);

    #endregion
}
