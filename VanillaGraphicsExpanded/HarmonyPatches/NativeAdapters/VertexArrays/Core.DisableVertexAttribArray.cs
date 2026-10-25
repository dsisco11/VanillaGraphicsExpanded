using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.DisableVertexAttribArray overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DisableVertexAttribArray(int @index) => StateCache.Current.ExecuteCoreDisableVertexAttribArray(@index);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DisableVertexAttribArray(uint @index) => StateCache.Current.ExecuteCoreDisableVertexAttribArray(@index);

    #endregion
}
