using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.EnableVertexArrayAttrib overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EnableVertexArrayAttrib(int @vaobj, int @index) => StateCache.Current.ExecuteCoreEnableVertexArrayAttrib(@vaobj, @index);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EnableVertexArrayAttrib(uint @vaobj, uint @index) => StateCache.Current.ExecuteCoreEnableVertexArrayAttrib(@vaobj, @index);

    #endregion
}
