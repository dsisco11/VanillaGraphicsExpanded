using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Arb.DisableVertexAttribArray overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DisableVertexAttribArray(int @index) => OpenGLVertexCalls.ExecuteArbDisableVertexAttribArray(@index);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DisableVertexAttribArray(uint @index) => OpenGLVertexCalls.ExecuteArbDisableVertexAttribArray(@index);

    #endregion
}
