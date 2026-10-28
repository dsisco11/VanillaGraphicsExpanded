using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Arb DrawBuffers dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLFramebufferRoutingCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteArbDrawBuffers(int @n, ref ArbDrawBuffers @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, ref @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n)) observation.Complete(returned, System.Runtime.InteropServices.MemoryMarshal.Cast<ArbDrawBuffers, DrawBuffersEnum>(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref @bufs, @n)));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteArbDrawBuffers(int @n, ArbDrawBuffers[] @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n) && @bufs is not null && @n <= @bufs.Length) observation.Complete(returned, System.Runtime.InteropServices.MemoryMarshal.Cast<ArbDrawBuffers, DrawBuffersEnum>(@bufs.AsSpan(0, @n)));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteArbDrawBuffers(int @n, ArbDrawBuffers* @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n) && (@n == 0 || @bufs != null)) observation.Complete(returned, System.Runtime.InteropServices.MemoryMarshal.Cast<ArbDrawBuffers, DrawBuffersEnum>(new ReadOnlySpan<ArbDrawBuffers>(@bufs, @n)));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteArbDrawBuffers(int @n, ref DrawBufferMode @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, ref @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n)) observation.Complete(returned, System.Runtime.InteropServices.MemoryMarshal.Cast<DrawBufferMode, DrawBuffersEnum>(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref @bufs, @n)));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteArbDrawBuffers(int @n, DrawBufferMode[] @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n) && @bufs is not null && @n <= @bufs.Length) observation.Complete(returned, System.Runtime.InteropServices.MemoryMarshal.Cast<DrawBufferMode, DrawBuffersEnum>(@bufs.AsSpan(0, @n)));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteArbDrawBuffers(int @n, DrawBufferMode* @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.DrawBuffers(@n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n) && (@n == 0 || @bufs != null)) observation.Complete(returned, System.Runtime.InteropServices.MemoryMarshal.Cast<DrawBufferMode, DrawBuffersEnum>(new ReadOnlySpan<DrawBufferMode>(@bufs, @n)));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }
    #endregion
}
