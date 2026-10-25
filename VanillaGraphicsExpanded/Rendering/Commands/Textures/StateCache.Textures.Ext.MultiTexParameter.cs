using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext MultiTexParameter execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
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
    internal void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
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
    internal void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
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
    internal void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, float @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
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
    internal void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, float[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
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
    internal void ExecuteExtMultiTexParameter(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, float* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
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
