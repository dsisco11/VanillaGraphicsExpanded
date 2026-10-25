using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void Uniform1(int @location, int @count, ref int @value)
        => StateCache.Current.ExecuteCoreUniform1(@location, @count, ref @value);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void Uniform1(int @location, int @count, int[] @value)
        => StateCache.Current.ExecuteCoreUniform1(@location, @count, @value);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void Uniform1(int @location, int @count, int* @value)
        => StateCache.Current.ExecuteCoreUniform1(@location, @count, @value);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void Uniform1(int @location, int @v0)
        => StateCache.Current.ExecuteCoreUniform1(@location, @v0);
    #endregion
}
