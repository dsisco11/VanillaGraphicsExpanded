using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.EnableVertexArrayAttrib overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EnableVertexArrayAttrib(int @vaobj, int @index) => StateCache.Current.ExecuteExtEnableVertexArrayAttrib(@vaobj, @index);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EnableVertexArrayAttrib(uint @vaobj, uint @index) => StateCache.Current.ExecuteExtEnableVertexArrayAttrib(@vaobj, @index);

    #endregion
}
