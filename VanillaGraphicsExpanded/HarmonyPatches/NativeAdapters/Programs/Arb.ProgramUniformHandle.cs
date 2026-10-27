using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Arb overloads through their command execution boundary.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniformHandle(int @program, int @location, int @count, ref long @values)
        => OpenGLProgramCalls.ExecuteArbProgramUniformHandle(@program, @location, @count, ref @values);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniformHandle(int @program, int @location, int @count, long[] @values)
        => OpenGLProgramCalls.ExecuteArbProgramUniformHandle(@program, @location, @count, @values);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniformHandle(int @program, int @location, int @count, long* @values)
        => OpenGLProgramCalls.ExecuteArbProgramUniformHandle(@program, @location, @count, @values);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniformHandle(int @program, int @location, long @value)
        => OpenGLProgramCalls.ExecuteArbProgramUniformHandle(@program, @location, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniformHandle(uint @program, int @location, int @count, ref ulong @values)
        => OpenGLProgramCalls.ExecuteArbProgramUniformHandle(@program, @location, @count, ref @values);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniformHandle(uint @program, int @location, int @count, ulong[] @values)
        => OpenGLProgramCalls.ExecuteArbProgramUniformHandle(@program, @location, @count, @values);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniformHandle(uint @program, int @location, int @count, ulong* @values)
        => OpenGLProgramCalls.ExecuteArbProgramUniformHandle(@program, @location, @count, @values);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramUniformHandle(uint @program, int @location, ulong @value)
        => OpenGLProgramCalls.ExecuteArbProgramUniformHandle(@program, @location, @value);
    #endregion
}
