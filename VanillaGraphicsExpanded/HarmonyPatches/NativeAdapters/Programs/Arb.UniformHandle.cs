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
    internal static void UniformHandle(int @location, int @count, ref long @value)
        => OpenGLProgramCalls.ExecuteArbUniformHandle(@location, @count, ref @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UniformHandle(int @location, int @count, long[] @value)
        => OpenGLProgramCalls.ExecuteArbUniformHandle(@location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UniformHandle(int @location, int @count, long* @value)
        => OpenGLProgramCalls.ExecuteArbUniformHandle(@location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UniformHandle(int @location, int @count, ref ulong @value)
        => OpenGLProgramCalls.ExecuteArbUniformHandle(@location, @count, ref @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UniformHandle(int @location, int @count, ulong[] @value)
        => OpenGLProgramCalls.ExecuteArbUniformHandle(@location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UniformHandle(int @location, int @count, ulong* @value)
        => OpenGLProgramCalls.ExecuteArbUniformHandle(@location, @count, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UniformHandle(int @location, long @value)
        => OpenGLProgramCalls.ExecuteArbUniformHandle(@location, @value);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void UniformHandle(int @location, ulong @value)
        => OpenGLProgramCalls.ExecuteArbUniformHandle(@location, @value);
    #endregion
}
