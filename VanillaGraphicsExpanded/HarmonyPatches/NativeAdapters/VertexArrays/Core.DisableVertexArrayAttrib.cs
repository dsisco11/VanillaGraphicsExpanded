using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.DisableVertexArrayAttrib overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DisableVertexArrayAttrib(int @vaobj, int @index) => StateCache.Current.ExecuteCoreDisableVertexArrayAttrib(@vaobj, @index);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DisableVertexArrayAttrib(uint @vaobj, uint @index) => StateCache.Current.ExecuteCoreDisableVertexArrayAttrib(@vaobj, @index);

    #endregion
}
