using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexParameterI overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, ref int @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameterI(@target, @pname, ref @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, int[] @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameterI(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, int* @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameterI(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, ref uint @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameterI(@target, @pname, ref @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, uint[] @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameterI(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, uint* @params)
        => OpenGLTexturesCalls.ExecuteCoreTexParameterI(@target, @pname, @params);

    #endregion
}
