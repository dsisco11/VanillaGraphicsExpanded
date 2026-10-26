using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureParameter overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, int @param)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, int[] @params)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, int* @params)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, float @param)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, float[] @params)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, float* @params)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, int @param)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, int[] @params)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, int* @params)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, float @param)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, float[] @params)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, float* @params)
        => OpenGLTexturesCalls.ExecuteExtTextureParameter(@texture, @target, @pname, @params);

    #endregion
}
