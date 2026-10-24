using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.NamedFramebufferReadBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferReadBuffer(int @framebuffer, ReadBufferMode @src)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, true);
        bool returned = false;
        try
        {
            GL.NamedFramebufferReadBuffer(@framebuffer, @src);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedFramebufferReadBuffer(uint @framebuffer, ReadBufferMode @src)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, true);
        bool returned = false;
        try
        {
            GL.NamedFramebufferReadBuffer(@framebuffer, @src);
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
