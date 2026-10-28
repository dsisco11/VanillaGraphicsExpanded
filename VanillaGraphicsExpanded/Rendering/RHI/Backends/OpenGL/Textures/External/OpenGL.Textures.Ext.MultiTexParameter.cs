using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext MultiTexParameter execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLTexturesCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameter(@texunit, @target, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int[] @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params is { Length: > 0 } ? @params[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameter(@texunit, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int* @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params != null ? @params[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameter(@texunit, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, float @param)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (ConvertTextureParameter(@param)) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameter(@texunit, @target, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, float[] @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params is { Length: > 0 } ? ConvertTextureParameter(@params[0]) : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameter(@texunit, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, float* @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params != null ? ConvertTextureParameter(@params[0]) : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameter(@texunit, @target, @pname, @params);
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
