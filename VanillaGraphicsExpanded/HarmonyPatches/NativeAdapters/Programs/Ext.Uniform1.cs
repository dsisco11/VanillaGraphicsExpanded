using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Ext overloads through their command execution boundary.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void Uniform1(int @location, int @count, ref int @value)
        => OpenGLProgramCalls.ExecuteExtUniform1(@location, @count, ref @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void Uniform1(int @location, int @count, int[] @value)
        => OpenGLProgramCalls.ExecuteExtUniform1(@location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void Uniform1(int @location, int @count, int* @value)
        => OpenGLProgramCalls.ExecuteExtUniform1(@location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void Uniform1(int @location, int @v0)
        => OpenGLProgramCalls.ExecuteExtUniform1(@location, @v0);
    #endregion
}
