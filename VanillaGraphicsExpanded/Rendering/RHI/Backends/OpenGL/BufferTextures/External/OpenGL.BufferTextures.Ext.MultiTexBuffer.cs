using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext MultiTexBuffer execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLBufferTexturesCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexBuffer(TextureUnit @texunit, TextureTarget @target, InternalFormat @internalformat, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), unchecked((int)@texunit), false, true, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexBuffer(@texunit, @target, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexBuffer(TextureUnit @texunit, TextureTarget @target, InternalFormat @internalformat, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), unchecked((int)@texunit), false, true, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexBuffer(@texunit, @target, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexBuffer(TextureUnit @texunit, TextureTarget @target, TypeEnum @internalformat, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), unchecked((int)@texunit), false, true, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexBuffer(@texunit, @target, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtMultiTexBuffer(TextureUnit @texunit, TextureTarget @target, TypeEnum @internalformat, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), unchecked((int)@texunit), false, true, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.Ext.MultiTexBuffer(@texunit, @target, @internalformat, @buffer);
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
