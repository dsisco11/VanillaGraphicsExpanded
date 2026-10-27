using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.MultiTexRenderbuffer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexRenderbuffer(TextureUnit @texunit, TextureTarget @target, int @renderbuffer)
        => OpenGLTexturesCalls.ExecuteExtMultiTexRenderbuffer(@texunit, @target, @renderbuffer);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexRenderbuffer(TextureUnit @texunit, TextureTarget @target, uint @renderbuffer)
        => OpenGLTexturesCalls.ExecuteExtMultiTexRenderbuffer(@texunit, @target, @renderbuffer);

    #endregion
}
