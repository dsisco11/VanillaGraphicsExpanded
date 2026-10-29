using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes post-call draw routing into the existing framebuffer-local cache.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Observes equivalent bound multi-output commands without intermediate receipts.</summary>
    [InterceptOpenGL("glDrawBuffers")]
    [InterceptOpenGL("glDrawBuffersARB")]
    internal static unsafe void ObserveDrawBuffers(int count, int* buffers)
        {
        if (OpenGLObservationContext.TryGet(out var cache, out _)) cache.ObserveDrawRouting(null, count, buffers, false);
    }

    /// <summary>Observes equivalent named multi-output commands using their explicit framebuffer.</summary>
    [InterceptOpenGL("glNamedFramebufferDrawBuffers")]
    internal static unsafe void ObserveNamedDrawBuffers(uint framebuffer, int count, int* buffers)
        {
        if (OpenGLObservationContext.TryGet(out var cache, out _)) cache.ObserveDrawRouting(unchecked((int)framebuffer), count, buffers, false);
    }

    /// <summary>Observes EXT routing after its named framebuffer realization.</summary>
    [InterceptOpenGL("glFramebufferDrawBuffersEXT")]
    internal static unsafe void ObserveExtNamedDrawBuffers(uint framebuffer, int count, int* buffers)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _)) return;
        cache.RealizeFramebuffer(unchecked((int)framebuffer), implicitCreation: true);
        cache.ObserveDrawRouting(unchecked((int)framebuffer), count, buffers, false);
    }

    /// <summary>Preserves the distinct grouped window-buffer semantics of a scalar command.</summary>
    [InterceptOpenGL("glDrawBuffer")]
    internal static unsafe void ObserveDrawBuffer(int buffer)
        {
        if (OpenGLObservationContext.TryGet(out var cache, out _)) cache.ObserveDrawRouting(null, 1, &buffer, true);
    }

    /// <summary>Observes equivalent named scalar commands without querying native state.</summary>
    [InterceptOpenGL("glNamedFramebufferDrawBuffer")]
    internal static unsafe void ObserveNamedDrawBuffer(uint framebuffer, int buffer)
        {
        if (OpenGLObservationContext.TryGet(out var cache, out _)) cache.ObserveDrawRouting(unchecked((int)framebuffer), 1, &buffer, true);
    }

    /// <summary>Observes EXT scalar routing after its named framebuffer realization.</summary>
    [InterceptOpenGL("glFramebufferDrawBufferEXT")]
    internal static unsafe void ObserveExtNamedDrawBuffer(uint framebuffer, int buffer)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _)) return;
        cache.RealizeFramebuffer(unchecked((int)framebuffer), implicitCreation: true);
        cache.ObserveDrawRouting(unchecked((int)framebuffer), 1, &buffer, true);
    }
    #endregion

    #region Private
    /// <summary>Leaves known rejected inputs unchanged and publishes only established routing effects.</summary>
    private unsafe void ObserveDrawRouting(int? namedFramebuffer, int count, int* buffers, bool single)
    {
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext)
        {
            if (namedFramebuffer.HasValue) InvalidateFramebufferDrawRouting(namedFramebuffer.Value);
            else if (TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out int current))
                InvalidateFramebufferDrawRouting(current);
            return;
        }
        var limits = GpuSupport.Graphics;
        // Invalid counts are rejected by GL; never dereference their argument pointer.
        if (count < 0 || count > limits.MaxDrawBuffers) return;
        int framebuffer = namedFramebuffer ?? GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer);
        bool realized = namedFramebuffer is null || framebuffer == 0 || framebufferDrawRouting.ContainsKey(framebuffer)
            || TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out int selected) && selected == framebuffer
            || RHIResourceManagerSystem.CurrentManager is { CanObserveNativeMetadata: true } manager
                && manager.FindObservedResource(GpuResourceKind.Framebuffer, framebuffer) is { State: GpuResourceRegistrationState.Live };
        if (!realized || count != 0 && buffers == null) { InvalidateFramebufferDrawRouting(framebuffer); return; }
        var route = new ReadOnlySpan<DrawBuffersEnum>(buffers, count);
        for (int i = 0; i < route.Length; i++)
        {
            var value = route[i];
            if (value == DrawBuffersEnum.None) continue;
            if (framebuffer != 0)
            {
                if (value < DrawBuffersEnum.ColorAttachment0
                    || (int)value - (int)DrawBuffersEnum.ColorAttachment0 >= limits.MaxColorAttachments) return;
            }
            else
            {
                bool? accepted = WindowDrawBufferAccepted((DrawBufferMode)value, single, limits.DoubleBuffered, limits.Stereo);
                if (accepted == false) return;
                if (accepted is null) { InvalidateFramebufferDrawRouting(framebuffer); return; }
            }
            for (int earlier = 0; earlier < i; earlier++) if (route[earlier] == value) return;
        }
        RememberFramebufferDrawRouting(framebuffer, route);
    }

    /// <summary>Uses context-discovery facts to distinguish unavailable and uncertain window selectors.</summary>
    private static bool? WindowDrawBufferAccepted(DrawBufferMode buffer, bool single, bool? doubleBuffered, bool? stereo) => buffer switch
    {
        DrawBufferMode.None or DrawBufferMode.FrontLeft => true,
        DrawBufferMode.FrontRight => stereo,
        DrawBufferMode.BackLeft => doubleBuffered,
        DrawBufferMode.BackRight => doubleBuffered == false || stereo == false ? false
            : doubleBuffered == true && stereo == true ? true : null,
        DrawBufferMode.Front or DrawBufferMode.Left or DrawBufferMode.FrontAndBack => single,
        DrawBufferMode.Back => single ? doubleBuffered : false,
        DrawBufferMode.Right => single ? stereo : false,
        _ => false
    };
    #endregion
}
