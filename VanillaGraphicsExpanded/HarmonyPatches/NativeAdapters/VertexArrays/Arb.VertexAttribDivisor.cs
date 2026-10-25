using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Arb.VertexAttribDivisor overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribDivisor(int @index, int @divisor) => StateCache.Current.ExecuteArbVertexAttribDivisor(@index, @divisor);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void VertexAttribDivisor(uint @index, uint @divisor) => StateCache.Current.ExecuteArbVertexAttribDivisor(@index, @divisor);

    #endregion
}
