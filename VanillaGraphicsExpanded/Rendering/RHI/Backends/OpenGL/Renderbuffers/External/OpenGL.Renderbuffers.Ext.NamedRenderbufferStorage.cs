using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext NamedRenderbufferStorage execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLRenderbuffersCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedRenderbufferStorage(int @renderbuffer, ExtDirectStateAccess @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedRenderbufferStorage(int @renderbuffer, PixelInternalFormat @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedRenderbufferStorage(int @renderbuffer, RenderbufferStorage @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedRenderbufferStorage(uint @renderbuffer, ExtDirectStateAccess @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedRenderbufferStorage(uint @renderbuffer, PixelInternalFormat @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedRenderbufferStorage(uint @renderbuffer, RenderbufferStorage @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);
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
