using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.UseProgram overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UseProgram(int @program) => OpenGLProgramCalls.ExecuteCoreUseProgram(@program);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UseProgram(uint @program) => OpenGLProgramCalls.ExecuteCoreUseProgram(@program);

    #endregion
}
