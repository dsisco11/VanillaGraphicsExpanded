using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.Ext.FramebufferDrawBuffers overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void FramebufferDrawBuffers(int @framebuffer, int @n, ref DrawBufferMode @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferDrawBuffers(@framebuffer, @n, ref @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void FramebufferDrawBuffers(int @framebuffer, int @n, DrawBufferMode[] @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferDrawBuffers(@framebuffer, @n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void FramebufferDrawBuffers(int @framebuffer, int @n, DrawBufferMode* @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferDrawBuffers(@framebuffer, @n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void FramebufferDrawBuffers(uint @framebuffer, int @n, ref DrawBufferMode @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferDrawBuffers(@framebuffer, @n, ref @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void FramebufferDrawBuffers(uint @framebuffer, int @n, DrawBufferMode[] @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferDrawBuffers(@framebuffer, @n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void FramebufferDrawBuffers(uint @framebuffer, int @n, DrawBufferMode* @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferDrawBuffers(@framebuffer, @n, @bufs);
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
