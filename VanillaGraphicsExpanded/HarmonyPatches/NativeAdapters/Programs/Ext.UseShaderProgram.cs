using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Ext.UseShaderProgram overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UseShaderProgram(ExtSeparateShaderObjects @type, int @program) => OpenGLProgramCalls.ExecuteExtUseShaderProgram(@type, @program);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UseShaderProgram(ExtSeparateShaderObjects @type, uint @program) => OpenGLProgramCalls.ExecuteExtUseShaderProgram(@type, @program);

    #endregion
}
