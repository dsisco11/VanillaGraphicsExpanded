using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexArrayElementBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayElementBuffer(int @vaobj, int @buffer) => OpenGLVertexCalls.ExecuteCoreVertexArrayElementBuffer(@vaobj, @buffer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void VertexArrayElementBuffer(uint @vaobj, uint @buffer) => OpenGLVertexCalls.ExecuteCoreVertexArrayElementBuffer(@vaobj, @buffer);

    #endregion
}
