using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.MultiTexParameterI overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, ref int @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameterI(@texunit, @target, @pname, ref @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int[] @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameterI(@texunit, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int* @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameterI(@texunit, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, ref uint @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameterI(@texunit, @target, @pname, ref @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, uint[] @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameterI(@texunit, @target, @pname, @params);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void MultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, uint* @params)
        => OpenGLTexturesCalls.ExecuteExtMultiTexParameterI(@texunit, @target, @pname, @params);

    #endregion
}
