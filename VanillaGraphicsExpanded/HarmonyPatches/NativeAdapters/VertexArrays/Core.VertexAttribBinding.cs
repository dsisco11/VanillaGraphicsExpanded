using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexAttribBinding overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribBinding(int @attribindex, int @bindingindex) => StateCache.Current.ExecuteCoreVertexAttribBinding(@attribindex, @bindingindex);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribBinding(uint @attribindex, uint @bindingindex) => StateCache.Current.ExecuteCoreVertexAttribBinding(@attribindex, @bindingindex);

    #endregion
}
