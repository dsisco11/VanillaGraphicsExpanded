using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.Arb.DrawBuffers overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void DrawBuffers(int @n, ref ArbDrawBuffers @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, ref @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void DrawBuffers(int @n, ArbDrawBuffers[] @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void DrawBuffers(int @n, ArbDrawBuffers* @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void DrawBuffers(int @n, ref DrawBufferMode @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, ref @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void DrawBuffers(int @n, DrawBufferMode[] @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void DrawBuffers(int @n, DrawBufferMode* @bufs)
    {
        var observation = StateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, @bufs);
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
