using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Ext.UseShaderProgram overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void UseShaderProgram(ExtSeparateShaderObjects @type, int @program) => StateCache.Current.ExecuteExtUseShaderProgram(@type, @program);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void UseShaderProgram(ExtSeparateShaderObjects @type, uint @program) => StateCache.Current.ExecuteExtUseShaderProgram(@type, @program);

    #endregion
}
