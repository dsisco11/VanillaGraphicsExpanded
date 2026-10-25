using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Arb overloads through their command execution boundary.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void Uniform1(int @location, int @count, ref int @value)
        => StateCache.Current.ExecuteArbUniform1(@location, @count, ref @value);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void Uniform1(int @location, int @count, int[] @value)
        => StateCache.Current.ExecuteArbUniform1(@location, @count, @value);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void Uniform1(int @location, int @count, int* @value)
        => StateCache.Current.ExecuteArbUniform1(@location, @count, @value);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void Uniform1(int @location, int @v0)
        => StateCache.Current.ExecuteArbUniform1(@location, @v0);
    #endregion
}
