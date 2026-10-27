using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Ext.BindProgramPipeline overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void BindProgramPipeline(int @pipeline) => OpenGLProgramCalls.ExecuteExtBindProgramPipeline(@pipeline);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void BindProgramPipeline(uint @pipeline) => OpenGLProgramCalls.ExecuteExtBindProgramPipeline(@pipeline);

    #endregion
}
