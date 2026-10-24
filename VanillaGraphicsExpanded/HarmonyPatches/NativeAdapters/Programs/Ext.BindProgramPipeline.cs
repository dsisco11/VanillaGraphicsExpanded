using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Ext.BindProgramPipeline overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindProgramPipeline(int @pipeline) => StateCache.Current.ExecuteExtBindProgramPipeline(@pipeline);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindProgramPipeline(uint @pipeline) => StateCache.Current.ExecuteExtBindProgramPipeline(@pipeline);

    #endregion
}
