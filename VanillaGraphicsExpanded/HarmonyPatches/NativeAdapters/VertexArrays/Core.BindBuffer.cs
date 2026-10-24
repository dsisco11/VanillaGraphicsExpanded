using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.BindBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindBuffer(BufferTarget @target, int @buffer) => StateCache.Current.ExecuteCoreBindBuffer(@target, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void BindBuffer(BufferTarget @target, uint @buffer) => StateCache.Current.ExecuteCoreBindBuffer(@target, @buffer);

    #endregion
}
