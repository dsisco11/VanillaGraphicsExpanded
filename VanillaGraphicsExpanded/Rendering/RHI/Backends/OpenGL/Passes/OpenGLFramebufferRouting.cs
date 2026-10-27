using System;
using OpenTK.Graphics.OpenGL4;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes controlled framebuffer-local routing while preserving its single executed-state cache.</summary>
internal static class OpenGLFramebufferRouting
{
    #region Internal API
    /// <summary>Captures an independently owned route, reading native state only when no declaration or executed knowledge exists.</summary>
    internal static Legacy.DrawBuffersEnum[] Capture(OpenGlStateCache cache, int framebuffer)
    {
        if (cache.TryCopyFramebufferDrawRouting(framebuffer, out var route)) return route;
        if (!cache.TryGetCachedCurrentFramebuffer(Legacy.FramebufferTarget.DrawFramebuffer, out int selected) || selected != framebuffer)
            throw new InvalidOperationException("Routing acquisition requires the selected original draw target.");
        route = new Legacy.DrawBuffersEnum[GpuSupport.Graphics.MaxDrawBuffers];
        for (int i = 0; i < route.Length; i++)
        {
            int value = -1;
            GL.GetInteger(GetPName.DrawBuffer0 + i, out value);
            if (value < 0) throw new InvalidOperationException("Framebuffer routing is unavailable.");
            route[i] = (Legacy.DrawBuffersEnum)value;
        }
        cache.RememberFramebufferDrawRouting(framebuffer, route);
        return route;
    }
    /// <summary>Publishes an explicit complete route for the selected controlled draw framebuffer.</summary>
    internal static void SetDrawBuffers(OpenGlStateCache cache, int count, Legacy.DrawBuffersEnum[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (count < 0 || count > buffers.Length || count > GpuSupport.Graphics.MaxDrawBuffers) throw new ArgumentOutOfRangeException(nameof(count));
        Apply(cache, cache.GetCurrentFramebuffer(Legacy.FramebufferTarget.DrawFramebuffer), buffers.AsSpan(0, count), false);
    }
    /// <summary>Publishes a single selector, preserving grouped default-window selector semantics.</summary>
    internal static void SetDrawBuffer(OpenGlStateCache cache, Legacy.DrawBufferMode buffer)
        => Apply(cache, cache.GetCurrentFramebuffer(Legacy.FramebufferTarget.DrawFramebuffer), [(Legacy.DrawBuffersEnum)buffer], true);
    /// <summary>Restores one original target's complete route through named commands or its already selected window surface.</summary>
    internal static void Restore(OpenGlStateCache cache, int framebuffer, Legacy.DrawBuffersEnum[] route)
        => Apply(cache, framebuffer, route, framebuffer == 0 && OnlyFirstOutput(route));
    #endregion

    #region Private
    /// <summary>Compares established routing before allocating native arguments, then publishes only after originating-context return.</summary>
    private static void Apply(OpenGlStateCache cache, int framebuffer, ReadOnlySpan<Legacy.DrawBuffersEnum> route, bool single)
    {
        if (route.Length > GpuSupport.Graphics.MaxDrawBuffers) throw new ArgumentOutOfRangeException(nameof(route));
        // Immutable configurations carry their own proof even after ordinary binding-cache invalidation.
        if (cache.ValidateImmutableFramebufferRouting(framebuffer, route)) return;
        if (cache.MatchesFramebufferDrawRouting(framebuffer, route)) return;
        cache.InvalidateFramebufferDrawRouting(framebuffer);
        var origin = GpuResource.CaptureCreationContext(GpuResourceKind.Framebuffer);
        var native = new DrawBuffersEnum[Math.Max(1, route.Length)];
        for (int i = 0; i < route.Length; i++) native[i] = (DrawBuffersEnum)route[i];
        using (var errors = new GlDebug.ErrorScope("Framebuffer routing publication"))
        {
            if (framebuffer != 0)
            {
                if (single) GL.NamedFramebufferDrawBuffer(framebuffer, (DrawBufferMode)native[0]);
                else GL.NamedFramebufferDrawBuffers(framebuffer, native.Length, native);
            }
            else
            {
                if (!cache.TryGetCachedCurrentFramebuffer(Legacy.FramebufferTarget.DrawFramebuffer, out int selected) || selected != 0)
                    throw new InvalidOperationException("Window routing requires its selected surface.");
                if (single) GL.DrawBuffer((DrawBufferMode)native[0]);
                else GL.DrawBuffers(native.Length, native);
            }
        }
        if (!GpuResource.IsCurrentResourceContext(origin)) throw new InvalidOperationException("Framebuffer routing returned in a foreign context.");
        cache.RememberFramebufferDrawRouting(framebuffer, route);
    }
    /// <summary>Identifies single-route window restoration without changing grouped selector meaning.</summary>
    private static bool OnlyFirstOutput(ReadOnlySpan<Legacy.DrawBuffersEnum> route)
    {
        for (int i = 1; i < route.Length; i++) if (route[i] != Legacy.DrawBuffersEnum.None) return false;
        return true;
    }
    #endregion
}
