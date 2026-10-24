using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Forwards GL.Ext.NamedFramebufferTextureFace overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferTextureFace(int @framebuffer, FramebufferAttachment @attachment, int @texture, int @level, TextureTarget @face)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferTextureFace(@framebuffer, @attachment, @texture, @level, @face);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferTextureFace(uint @framebuffer, FramebufferAttachment @attachment, uint @texture, int @level, TextureTarget @face)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@framebuffer), true, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
        bool returned = false;
        try
        {
            GL.Ext.NamedFramebufferTextureFace(@framebuffer, @attachment, @texture, @level, @face);
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
