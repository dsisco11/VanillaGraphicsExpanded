using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void UniformBlockBinding(int @program, int @uniformBlockIndex, int @uniformBlockBinding)
        => StateCache.Current.ExecuteCoreUniformBlockBinding(@program, @uniformBlockIndex, @uniformBlockBinding);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void UniformBlockBinding(uint @program, uint @uniformBlockIndex, uint @uniformBlockBinding)
        => StateCache.Current.ExecuteCoreUniformBlockBinding(@program, @uniformBlockIndex, @uniformBlockBinding);
    #endregion
}
