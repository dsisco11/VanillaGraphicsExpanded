using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TexParameter execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexParameter(TextureTarget @target, TextureParameterName @pname, int @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TexParameter(@target, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexParameter(TextureTarget @target, TextureParameterName @pname, int[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TexParameter(@target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexParameter(TextureTarget @target, TextureParameterName @pname, int* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TexParameter(@target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexParameter(TextureTarget @target, TextureParameterName @pname, float @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TexParameter(@target, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexParameter(TextureTarget @target, TextureParameterName @pname, float[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TexParameter(@target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexParameter(TextureTarget @target, TextureParameterName @pname, float* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TexParameter(@target, @pname, @params);
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
