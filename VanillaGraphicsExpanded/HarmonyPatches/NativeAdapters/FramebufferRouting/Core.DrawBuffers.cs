using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.DrawBuffers overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void DrawBuffers(int @n, ref DrawBuffersEnum @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.DrawBuffers(@n, ref @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void DrawBuffers(int @n, DrawBuffersEnum[] @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.DrawBuffers(@n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void DrawBuffers(int @n, DrawBuffersEnum* @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.DrawBuffers(@n, @bufs);
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
