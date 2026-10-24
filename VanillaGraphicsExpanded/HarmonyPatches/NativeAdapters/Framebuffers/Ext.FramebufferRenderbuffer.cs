using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Forwards GL.Ext.FramebufferRenderbuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void FramebufferRenderbuffer(FramebufferTarget @target, FramebufferAttachment @attachment, RenderbufferTarget @renderbuffertarget, int @renderbuffer)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), 0, 0, -1, 3);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferRenderbuffer(@target, @attachment, @renderbuffertarget, @renderbuffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void FramebufferRenderbuffer(FramebufferTarget @target, FramebufferAttachment @attachment, RenderbufferTarget @renderbuffertarget, uint @renderbuffer)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), 0, 0, -1, 3);
        bool returned = false;
        try
        {
            GL.Ext.FramebufferRenderbuffer(@target, @attachment, @renderbuffertarget, @renderbuffer);
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
