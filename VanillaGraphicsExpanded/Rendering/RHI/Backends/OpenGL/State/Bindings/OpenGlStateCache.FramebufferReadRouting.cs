using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains framebuffer-local read routing in the existing executed-state authority.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly Dictionary<int, ReadBufferMode> framebufferReadRouting = new();

    #region Internal API
    /// <summary>Records a completed named read selector without changing framebuffer bindings.</summary>
    internal void RememberFramebufferReadRouting(int framebuffer, ReadBufferMode route) => framebufferReadRouting[framebuffer] = route;

    /// <summary>Returns established read routing without consulting the driver.</summary>
    internal bool TryGetFramebufferReadRouting(int framebuffer, out ReadBufferMode route) => framebufferReadRouting.TryGetValue(framebuffer, out route);

    /// <summary>Establishes native read/draw defaults for a newly realized framebuffer.</summary>
    internal void RememberFramebufferDefaults(int framebuffer)
    {
        RememberFramebufferDrawRouting(framebuffer, [DrawBuffersEnum.ColorAttachment0]);
        RememberFramebufferReadRouting(framebuffer, ReadBufferMode.ColorAttachment0);
    }

    /// <summary>Realizes an observed reservation and installs routing defaults once on that original object.</summary>
    internal void RealizeFramebuffer(int framebuffer, bool implicitCreation = false)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        bool realized = manager?.FindObservedResource(GpuResourceKind.Framebuffer, framebuffer)?.IsRealized == true;
        var entry = manager?.RealizeObservedImage(GpuResourceKind.Framebuffer, framebuffer, implicitCreation: implicitCreation);
        // Unknown EXT objects can already have nondefault routing. Only a tracked
        // reservation whose defaults were just published establishes initial routes.
        if (!realized && entry?.IsUsable == true) RememberFramebufferDefaults(framebuffer);
    }
    #endregion
}
