using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.UseProgram overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void UseProgram(int @program) => StateCache.Current.ExecuteCoreUseProgram(@program);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void UseProgram(uint @program) => StateCache.Current.ExecuteCoreUseProgram(@program);

    #endregion
}
