using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Arb overloads through their command execution boundary.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void LinkProgram(int @programObj)
        => StateCache.Current.ExecuteArbLinkProgram(@programObj);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void LinkProgram(uint @programObj)
        => StateCache.Current.ExecuteArbLinkProgram(@programObj);
    #endregion
}
