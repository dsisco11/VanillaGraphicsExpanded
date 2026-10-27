using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.DisableVertexArrayAttrib overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DisableVertexArrayAttrib(int @vaobj, int @index) => OpenGLVertexCalls.ExecuteExtDisableVertexArrayAttrib(@vaobj, @index);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DisableVertexArrayAttrib(uint @vaobj, uint @index) => OpenGLVertexCalls.ExecuteExtDisableVertexArrayAttrib(@vaobj, @index);

    #endregion
}
