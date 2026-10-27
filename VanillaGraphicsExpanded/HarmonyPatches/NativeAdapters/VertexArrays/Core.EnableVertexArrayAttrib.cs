using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.EnableVertexArrayAttrib overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void EnableVertexArrayAttrib(int @vaobj, int @index) => OpenGLVertexCalls.ExecuteCoreEnableVertexArrayAttrib(@vaobj, @index);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void EnableVertexArrayAttrib(uint @vaobj, uint @index) => OpenGLVertexCalls.ExecuteCoreEnableVertexArrayAttrib(@vaobj, @index);

    #endregion
}
