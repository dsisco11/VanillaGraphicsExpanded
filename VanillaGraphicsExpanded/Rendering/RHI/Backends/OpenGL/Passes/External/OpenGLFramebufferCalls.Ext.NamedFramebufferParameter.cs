using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext NamedFramebufferParameter execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLFramebufferCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteExtNamedFramebufferParameter(int @framebuffer, ExtDirectStateAccess @pname, int @param)
    {
        var observation = StateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferParameter(@framebuffer, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteExtNamedFramebufferParameter(int @framebuffer, FramebufferDefaultParameter @pname, int @param)
    {
        var observation = StateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferParameter(@framebuffer, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteExtNamedFramebufferParameter(int @framebuffer, FramebufferParameterName @pname, int @param)
    {
        var observation = StateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferParameter(@framebuffer, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteExtNamedFramebufferParameter(uint @framebuffer, ExtDirectStateAccess @pname, int @param)
    {
        var observation = StateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferParameter(@framebuffer, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteExtNamedFramebufferParameter(uint @framebuffer, FramebufferDefaultParameter @pname, int @param)
    {
        var observation = StateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferParameter(@framebuffer, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteExtNamedFramebufferParameter(uint @framebuffer, FramebufferParameterName @pname, int @param)
    {
        var observation = StateCache.BeginNativeFramebufferParameter(unchecked((int)@framebuffer), true, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferParameter(@framebuffer, @pname, @param);
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
