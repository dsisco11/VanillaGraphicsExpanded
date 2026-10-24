using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Arb.BindBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindBuffer(BufferTargetArb @target, int @buffer) => StateCache.Current.ExecuteArbBindBuffer(@target, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindBuffer(BufferTargetArb @target, uint @buffer) => StateCache.Current.ExecuteArbBindBuffer(@target, @buffer);

    #endregion
}
