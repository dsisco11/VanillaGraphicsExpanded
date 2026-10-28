using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core NamedFramebufferDrawBuffers dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLFramebufferRoutingCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferDrawBuffers(int @framebuffer, int @n, ref DrawBuffersEnum @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.NamedFramebufferDrawBuffers(@framebuffer, @n, ref @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n)) observation.Complete(returned, System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref @bufs, @n));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferDrawBuffers(int @framebuffer, int @n, DrawBuffersEnum[] @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.NamedFramebufferDrawBuffers(@framebuffer, @n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n) && @bufs is not null && @n <= @bufs.Length) observation.Complete(returned, @bufs.AsSpan(0, @n));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferDrawBuffers(int @framebuffer, int @n, DrawBuffersEnum* @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.NamedFramebufferDrawBuffers(@framebuffer, @n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n) && (@n == 0 || @bufs != null)) observation.Complete(returned, new ReadOnlySpan<DrawBuffersEnum>(@bufs, @n));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferDrawBuffers(uint @framebuffer, int @n, ref DrawBuffersEnum @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.NamedFramebufferDrawBuffers(@framebuffer, @n, ref @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n)) observation.Complete(returned, System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref @bufs, @n));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferDrawBuffers(uint @framebuffer, int @n, DrawBuffersEnum[] @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.NamedFramebufferDrawBuffers(@framebuffer, @n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n) && @bufs is not null && @n <= @bufs.Length) observation.Complete(returned, @bufs.AsSpan(0, @n));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreNamedFramebufferDrawBuffers(uint @framebuffer, int @n, DrawBuffersEnum* @bufs)
    {
        var observation = OpenGlStateCache.BeginNativeFramebufferRouting(unchecked((int)@framebuffer), true, false);
        bool returned = false;
        try
        {
            GL.NamedFramebufferDrawBuffers(@framebuffer, @n, @bufs);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            if (observation.CanReadArguments(@n) && (@n == 0 || @bufs != null)) observation.Complete(returned, new ReadOnlySpan<DrawBuffersEnum>(@bufs, @n));
            else observation.Complete(returned && observation.IsRejectedCount(@n));
        }
    }
    #endregion
}
