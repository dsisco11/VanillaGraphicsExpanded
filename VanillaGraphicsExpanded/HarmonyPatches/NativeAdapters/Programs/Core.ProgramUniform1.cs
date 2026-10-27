using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniform1(int @program, int @location, int @count, ref int @value)
        => OpenGLProgramCalls.ExecuteCoreProgramUniform1(@program, @location, @count, ref @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniform1(int @program, int @location, int @count, int[] @value)
        => OpenGLProgramCalls.ExecuteCoreProgramUniform1(@program, @location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniform1(int @program, int @location, int @count, int* @value)
        => OpenGLProgramCalls.ExecuteCoreProgramUniform1(@program, @location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniform1(int @program, int @location, int @v0)
        => OpenGLProgramCalls.ExecuteCoreProgramUniform1(@program, @location, @v0);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniform1(uint @program, int @location, int @count, ref int @value)
        => OpenGLProgramCalls.ExecuteCoreProgramUniform1(@program, @location, @count, ref @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniform1(uint @program, int @location, int @count, int[] @value)
        => OpenGLProgramCalls.ExecuteCoreProgramUniform1(@program, @location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniform1(uint @program, int @location, int @count, int* @value)
        => OpenGLProgramCalls.ExecuteCoreProgramUniform1(@program, @location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniform1(uint @program, int @location, int @v0)
        => OpenGLProgramCalls.ExecuteCoreProgramUniform1(@program, @location, @v0);
    #endregion
}
