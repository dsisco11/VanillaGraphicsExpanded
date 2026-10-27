using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void LinkProgram(int @program)
        => StateCache.Current.ExecuteCoreLinkProgram(@program);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void LinkProgram(uint @program)
        => StateCache.Current.ExecuteCoreLinkProgram(@program);
    #endregion
}
