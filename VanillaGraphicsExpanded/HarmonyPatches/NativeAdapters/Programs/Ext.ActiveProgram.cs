using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Ext.ActiveProgram overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void ActiveProgram(int @program) => StateCache.Current.ExecuteExtActiveProgram(@program);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void ActiveProgram(uint @program) => StateCache.Current.ExecuteExtActiveProgram(@program);

    #endregion
}
