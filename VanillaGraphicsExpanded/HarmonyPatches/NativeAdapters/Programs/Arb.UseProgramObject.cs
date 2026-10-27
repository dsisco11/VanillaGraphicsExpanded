using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Arb.UseProgramObject overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UseProgramObject(int @programObj) => OpenGLProgramCalls.ExecuteArbUseProgramObject(@programObj);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UseProgramObject(uint @programObj) => OpenGLProgramCalls.ExecuteArbUseProgramObject(@programObj);

    #endregion
}
