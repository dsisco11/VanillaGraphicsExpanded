using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core TextureParameter execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLTexturesCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, int @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, int[] @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param is { Length: > 0 } ? @param[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, int* @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param != null ? @param[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, float @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (ConvertTextureParameter(@param)) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, float[] @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param is { Length: > 0 } ? ConvertTextureParameter(@param[0]) : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, float* @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param != null ? ConvertTextureParameter(@param[0]) : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, int @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, int[] @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param is { Length: > 0 } ? @param[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, int* @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param != null ? @param[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, float @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (ConvertTextureParameter(@param)) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, float[] @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param is { Length: > 0 } ? ConvertTextureParameter(@param[0]) : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, float* @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param != null ? ConvertTextureParameter(@param[0]) : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }
    #endregion
}
