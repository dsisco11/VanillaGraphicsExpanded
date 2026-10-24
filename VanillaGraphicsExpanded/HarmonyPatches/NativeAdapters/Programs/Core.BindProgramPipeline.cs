using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.BindProgramPipeline overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindProgramPipeline(int @pipeline) => StateCache.Current.ExecuteCoreBindProgramPipeline(@pipeline);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindProgramPipeline(uint @pipeline) => StateCache.Current.ExecuteCoreBindProgramPipeline(@pipeline);

    #endregion
}
