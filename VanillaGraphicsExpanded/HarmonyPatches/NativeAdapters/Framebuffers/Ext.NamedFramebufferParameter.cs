using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Forwards GL.Ext.NamedFramebufferParameter overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferParameter(int @framebuffer, ExtDirectStateAccess @pname, int @param)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferParameter(int @framebuffer, FramebufferDefaultParameter @pname, int @param)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferParameter(int @framebuffer, FramebufferParameterName @pname, int @param)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferParameter(uint @framebuffer, ExtDirectStateAccess @pname, int @param)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferParameter(uint @framebuffer, FramebufferDefaultParameter @pname, int @param)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferParameter(uint @framebuffer, FramebufferParameterName @pname, int @param)
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
