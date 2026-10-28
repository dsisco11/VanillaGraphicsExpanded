using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates established external draw routing into the existing framebuffer-local authority.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Admits configuration without discarding a still-valid route before exact native dispatch.</summary>
    internal static NativeFramebufferRouting BeginNativeFramebufferRouting(int selector, bool named, bool read)
    {
        // Read routing has no cached mirror and cannot make draw routing unknown.
        if (read) return default;
        var cache = EngineStateCalls.CurrentCache;
        int framebuffer = named ? selector : cache.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer);
        cache.RequireMutableFramebufferConfiguration(framebuffer);
        // A selected name is realized. Unknown named targets require initial acquisition rather
        // than treating a silent return as evidence that the name referred to an actual object.
        bool realized = !named || framebuffer == 0 || cache.framebufferDrawRouting.ContainsKey(framebuffer)
            || cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out int selected) && selected == framebuffer
            || GpuResourceManagerSystem.CurrentManager is { CanObserveNativeMetadata: true } manager
                && manager.FindObservedResource(GpuResourceKind.Framebuffer, framebuffer) is { State: GpuResourceRegistrationState.Live };
        var limits = GpuSupport.Graphics;
        cache.framebufferDrawRouting.Remove(framebuffer, out var previous);
        return new(cache, framebuffer, realized, Integration.RenderContextRegistry.CaptureResourceContext(),
            limits.MaxDrawBuffers, limits.MaxColorAttachments, previous, limits.DoubleBuffered, limits.Stereo);
    }
    #endregion

    /// <summary>Captures original routing authority while keeping command inputs and failure disposition explicit.</summary>
    internal readonly struct NativeFramebufferRouting(OpenGlStateCache? cache, int framebuffer, bool realized,
        (nint Handle, long Generation) origin, int maxDrawBuffers, int maxColorAttachments, DrawBuffersEnum[]? previous, bool? doubleBuffered, bool? stereo)
    {
        #region Internal API
        /// <summary>Bounds post-dispatch argument inspection using captured limits and the originating namespace.</summary>
        internal bool CanReadArguments(int count) => count >= 0 && count <= maxDrawBuffers
            && Integration.RenderContextRegistry.IsCurrentResourceContext(origin);

        /// <summary>Identifies counts the native API rejects without consulting a foreign context's limits.</summary>
        internal bool IsRejectedCount(int count) => count < 0 || count > maxDrawBuffers;

        /// <summary>Restores prior routing after proven rejection, or retains uncertainty after an interrupted dispatch.</summary>
        internal void Complete(bool nativeReturned)
        {
            if (cache?.OwnsKnowledgeFor(origin) != true) return;
            if (nativeReturned && Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) RestorePrevious();
            else cache.InvalidateFramebufferDrawRouting(framebuffer);
        }

        /// <summary>Publishes accepted multi-output arguments, or preserves the previous route for defined rejection.</summary>
        internal void Complete(bool nativeReturned, ReadOnlySpan<DrawBuffersEnum> route)
        {
            if (!nativeReturned || cache?.OwnsKnowledgeFor(origin) != true || !Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            { if (cache?.OwnsKnowledgeFor(origin) == true) cache.InvalidateFramebufferDrawRouting(framebuffer); return; }
            if (cache is null) return;
            if (!realized) { cache.InvalidateFramebufferDrawRouting(framebuffer); return; }
            if (route.Length > maxDrawBuffers) { RestorePrevious(); return; }
            for (int i = 0; i < route.Length; i++)
            {
                var value = route[i];
                if (value == DrawBuffersEnum.None) continue;
                if (framebuffer != 0)
                {
                    if (value < DrawBuffersEnum.ColorAttachment0
                        || (int)value - (int)DrawBuffersEnum.ColorAttachment0 >= maxColorAttachments) { RestorePrevious(); return; }
                }
                else
                {
                    bool? available = WindowBufferAccepted((DrawBufferMode)value, single: false);
                    if (available == false) { RestorePrevious(); return; }
                    if (available is null) { cache.InvalidateFramebufferDrawRouting(framebuffer); return; }
                }
                for (int earlier = 0; earlier < i; earlier++) if (route[earlier] == value) { RestorePrevious(); return; }
            }
            cache.RememberFramebufferDrawRouting(framebuffer, route);
        }

        /// <summary>Publishes a single object-local route without confusing grouped window-buffer selectors.</summary>
        internal void Complete(bool nativeReturned, DrawBufferMode buffer)
        {
            if (cache is null) return;
            if (!nativeReturned || cache?.OwnsKnowledgeFor(origin) != true || !Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            { if (cache.OwnsKnowledgeFor(origin)) cache.InvalidateFramebufferDrawRouting(framebuffer); return; }
            if (framebuffer == 0)
            {
                bool? available = WindowBufferAccepted(buffer, single: true);
                if (available == true) cache.RememberFramebufferDrawRouting(framebuffer, [(DrawBuffersEnum)buffer]);
                else if (available == false) RestorePrevious();
                else cache.InvalidateFramebufferDrawRouting(framebuffer);
                return;
            }
            Complete(nativeReturned, [(DrawBuffersEnum)buffer]);
        }
        #endregion

        #region Private
        /// <summary>Uses immutable context-discovery facts, retaining grouped scalar selectors exactly as authored.</summary>
        private bool? WindowBufferAccepted(DrawBufferMode buffer, bool single) => buffer switch
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

        /// <summary>Returns the retained private route after a defined rejection without allocation or native acquisition.</summary>
        private void RestorePrevious()
        {
            if (cache is null) return;
            if (previous is not null) cache.framebufferDrawRouting[framebuffer] = previous;
            else cache.framebufferDrawRouting.Remove(framebuffer);
        }
        #endregion
    }
}
