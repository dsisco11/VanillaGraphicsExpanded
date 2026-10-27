using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Ext.ActiveProgram overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ActiveProgram(int @program) => OpenGLProgramCalls.ExecuteExtActiveProgram(@program);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ActiveProgram(uint @program) => OpenGLProgramCalls.ExecuteExtActiveProgram(@program);

    #endregion
}
