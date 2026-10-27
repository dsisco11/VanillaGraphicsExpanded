using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.EnableVertexAttribArray overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void EnableVertexAttribArray(int @index) => OpenGLVertexCalls.ExecuteCoreEnableVertexAttribArray(@index);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void EnableVertexAttribArray(uint @index) => OpenGLVertexCalls.ExecuteCoreEnableVertexAttribArray(@index);

    #endregion
}
