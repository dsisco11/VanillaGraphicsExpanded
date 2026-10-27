using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexParameterI overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, ref int @params)
        => OpenGLTexturesCalls.ExecuteExtTexParameterI(@target, @pname, ref @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, int[] @params)
        => OpenGLTexturesCalls.ExecuteExtTexParameterI(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, int* @params)
        => OpenGLTexturesCalls.ExecuteExtTexParameterI(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, ref uint @params)
        => OpenGLTexturesCalls.ExecuteExtTexParameterI(@target, @pname, ref @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, uint[] @params)
        => OpenGLTexturesCalls.ExecuteExtTexParameterI(@target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexParameterI(TextureTarget @target, TextureParameterName @pname, uint* @params)
        => OpenGLTexturesCalls.ExecuteExtTexParameterI(@target, @pname, @params);

    #endregion
}
