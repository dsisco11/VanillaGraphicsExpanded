using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.DisableVertexArrayAttrib overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DisableVertexArrayAttrib(int @vaobj, int @index) => OpenGLVertexCalls.ExecuteCoreDisableVertexArrayAttrib(@vaobj, @index);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DisableVertexArrayAttrib(uint @vaobj, uint @index) => OpenGLVertexCalls.ExecuteCoreDisableVertexArrayAttrib(@vaobj, @index);

    #endregion
}
