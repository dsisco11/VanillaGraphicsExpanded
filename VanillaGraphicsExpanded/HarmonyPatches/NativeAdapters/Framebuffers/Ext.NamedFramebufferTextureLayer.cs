using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Forwards GL.Ext.NamedFramebufferTextureLayer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferTextureLayer(int @framebuffer, FramebufferAttachment @attachment, int @texture, int @level, int @layer)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), unchecked((int)@layer), 1);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferTextureLayer(@framebuffer, @attachment, @texture, @level, @layer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferTextureLayer(uint @framebuffer, FramebufferAttachment @attachment, uint @texture, int @level, int @layer)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), unchecked((int)@layer), 1);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferTextureLayer(@framebuffer, @attachment, @texture, @level, @layer);
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
