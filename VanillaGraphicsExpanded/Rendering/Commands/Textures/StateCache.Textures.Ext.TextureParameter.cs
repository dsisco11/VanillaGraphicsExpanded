using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext TextureParameter execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, int @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, int[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, int* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, float @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, float[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(int @texture, TextureTarget @target, TextureParameterName @pname, float* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, int @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, int[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, int* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, float @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, float[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtTextureParameter(uint @texture, TextureTarget @target, TextureParameterName @pname, float* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.TextureParameter(@texture, @target, @pname, @params);
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
