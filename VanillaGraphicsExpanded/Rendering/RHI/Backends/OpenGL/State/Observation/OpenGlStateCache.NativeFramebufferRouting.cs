using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes external routing without duplicating routing state in resource metadata.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Withdraws draw-route knowledge before forwarding a command; read routing has no cached mirror.</summary>
    internal static NativeFramebufferRouting BeginNativeFramebufferRouting(int selector, bool named, bool read)
    {
        // Read selectors are consumed directly by GL readback/blit operations. There is no
        // cached read selector to update and no resource descriptor changes to publish.
        if (read) return default;
        var cache = Current;
        int framebuffer = named ? selector : cache.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer);
        cache.RequireMutableFramebufferConfiguration(framebuffer);
        cache.InvalidateFramebufferDrawRouting(framebuffer);
        return new(cache, framebuffer);
    }
    #endregion

    /// <summary>Keeps external draw routing unknown until a consumer needs its actual resulting mapping.</summary>
    internal readonly struct NativeFramebufferRouting(OpenGlStateCache? cache, int framebuffer)
    {
        #region Internal API
        /// <summary>Retains unknown state after success or failure without consuming errors or guessing command acceptance.</summary>
        internal void Complete(bool nativeReturned)
        {
            // Native dispatch can reenter managed code; remove any route captured mid-command.
            cache?.InvalidateFramebufferDrawRouting(framebuffer);
        }
        #endregion
    }
}
