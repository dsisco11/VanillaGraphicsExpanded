using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.EnableVertexAttribArray overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EnableVertexAttribArray(int @index) => StateCache.Current.ExecuteCoreEnableVertexAttribArray(@index);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EnableVertexAttribArray(uint @index) => StateCache.Current.ExecuteCoreEnableVertexAttribArray(@index);

    #endregion
}
