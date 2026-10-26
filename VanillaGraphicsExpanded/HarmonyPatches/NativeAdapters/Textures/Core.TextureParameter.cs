using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TextureParameter overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureParameterName @pname, int @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureParameterName @pname, int[] @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureParameterName @pname, int* @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureParameterName @pname, float @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureParameterName @pname, float[] @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(int @texture, TextureParameterName @pname, float* @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureParameterName @pname, int @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureParameterName @pname, int[] @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureParameterName @pname, int* @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureParameterName @pname, float @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureParameterName @pname, float[] @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureParameter(uint @texture, TextureParameterName @pname, float* @param)
        => OpenGLTexturesCalls.ExecuteCoreTextureParameter(@texture, @pname, @param);

    #endregion
}
