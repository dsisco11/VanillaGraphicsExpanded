using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext MultiTexParameterI execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtMultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, ref int @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameterI(@texunit, @target, @pname, ref @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtMultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameterI(@texunit, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtMultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, int* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameterI(@texunit, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtMultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, ref uint @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameterI(@texunit, @target, @pname, ref @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtMultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, uint[] @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameterI(@texunit, @target, @pname, @params);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtMultiTexParameterI(TextureUnit @texunit, TextureTarget @target, TextureParameterName @pname, uint* @params)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.MultiTexParameterI(@texunit, @target, @pname, @params);
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
