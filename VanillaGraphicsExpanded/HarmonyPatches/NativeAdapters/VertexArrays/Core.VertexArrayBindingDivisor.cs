using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexArrayBindingDivisor overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayBindingDivisor(int @vaobj, int @bindingindex, int @divisor) => StateCache.Current.ExecuteCoreVertexArrayBindingDivisor(@vaobj, @bindingindex, @divisor);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexArrayBindingDivisor(uint @vaobj, uint @bindingindex, uint @divisor) => StateCache.Current.ExecuteCoreVertexArrayBindingDivisor(@vaobj, @bindingindex, @divisor);

    #endregion
}
