using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext TextureParameterI execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLTexturesCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureParameterI(int @texture, TextureTarget @target, TextureParameterName @pname, ref int @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.TextureParameterI(@texture, @target, @pname, ref @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureParameterI(int @texture, TextureTarget @target, TextureParameterName @pname, int[] @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params is { Length: > 0 } ? @params[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.TextureParameterI(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureParameterI(int @texture, TextureTarget @target, TextureParameterName @pname, int* @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params != null ? @params[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.TextureParameterI(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureParameterI(uint @texture, TextureTarget @target, TextureParameterName @pname, ref int @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.TextureParameterI(@texture, @target, @pname, ref @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureParameterI(uint @texture, TextureTarget @target, TextureParameterName @pname, int[] @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params is { Length: > 0 } ? @params[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.TextureParameterI(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureParameterI(uint @texture, TextureTarget @target, TextureParameterName @pname, int* @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params != null ? @params[0] : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.TextureParameterI(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureParameterI(uint @texture, TextureTarget @target, TextureParameterName @pname, ref uint @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? ((@params <= int.MaxValue ? (int?)@params : null)) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.TextureParameterI(@texture, @target, @pname, ref @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureParameterI(uint @texture, TextureTarget @target, TextureParameterName @pname, uint[] @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params is { Length: > 0 } ? (@params[0] <= int.MaxValue ? (int?)@params[0] : null) : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.TextureParameterI(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtTextureParameterI(uint @texture, TextureTarget @target, TextureParameterName @pname, uint* @params)
    {
        var observation = OpenGlStateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params != null ? (@params[0] <= int.MaxValue ? (int?)@params[0] : null) : (int?)null) : (int?)null);
        bool returned = false;
        try
        {
            GL.Ext.TextureParameterI(@texture, @target, @pname, @params);
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
