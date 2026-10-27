using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.MultiTexParameter overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int @param)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameter(@texunit, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int[] @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameter(@texunit, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int* @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameter(@texunit, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, float @param)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameter(@texunit, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, float[] @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameter(@texunit, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, float* @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameter(@texunit, @target, @pname, @params);

    #endregion
}
