using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexParameter overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, int @param)
        => OpenGLTexturesCalls.ExecuteCoreTexParameter(@target, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, int[] @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameter(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, int* @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameter(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, float @param)
        => OpenGLTexturesCalls.ExecuteCoreTexParameter(@target, @pname, @param);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, float[] @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameter(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameter(TextureTarget @target, TextureParameterName @pname, float* @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameter(@target, @pname, @params);

    #endregion
}
