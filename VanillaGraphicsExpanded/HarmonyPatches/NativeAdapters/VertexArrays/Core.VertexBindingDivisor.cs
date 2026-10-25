using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexBindingDivisor overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexBindingDivisor(int @bindingindex, int @divisor) => StateCache.Current.ExecuteCoreVertexBindingDivisor(@bindingindex, @divisor);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexBindingDivisor(uint @bindingindex, uint @divisor) => StateCache.Current.ExecuteCoreVertexBindingDivisor(@bindingindex, @divisor);

    #endregion
}
