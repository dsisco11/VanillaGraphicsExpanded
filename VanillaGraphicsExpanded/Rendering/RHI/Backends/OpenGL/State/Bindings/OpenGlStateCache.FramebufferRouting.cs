using System.Linq;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains framebuffer-local executed routes independently of resource metadata or native execution.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly Dictionary<int, DrawBuffersEnum[]> framebufferDrawRouting = new();

    #region Internal API
    /// <summary>Returns a private snapshot of routing established by execution or observation.</summary>
    internal bool TryCopyFramebufferDrawRouting(int framebuffer,out DrawBuffersEnum[] route)
    {
        if(framebufferDrawRouting.TryGetValue(framebuffer,out var stored))
        { route=(DrawBuffersEnum[])stored.Clone();return true; }
        route=[];return false;
    }
    /// <summary>Compares all effective slots without allocating normalized request storage on the suppression path.</summary>
    internal bool MatchesFramebufferDrawRouting(int framebuffer,ReadOnlySpan<DrawBuffersEnum> route)
    {
        if(!framebufferDrawRouting.TryGetValue(framebuffer,out var previous))return false;
        for(int i=0;i<Math.Max(previous.Length,route.Length);i++)
            if((i<previous.Length?previous[i]:DrawBuffersEnum.None)!=(i<route.Length?route[i]:DrawBuffersEnum.None))return false;
        return true;
    }
    /// <summary>Enforces immutable authored routing while suppressing only a matching executed route.</summary>
    internal bool ValidateImmutableFramebufferRouting(int framebuffer,ReadOnlySpan<DrawBuffersEnum> route)
    {
        if(!TryGetImmutableFramebuffer(framebuffer,out var immutable))return false;
        immutable.ValidateDeclaredRouting(route.ToArray().Select(value => value == DrawBuffersEnum.None ? -1 : (int)value - (int)DrawBuffersEnum.ColorAttachment0).ToArray());
        return MatchesFramebufferDrawRouting(framebuffer,route);
    }
    /// <summary>Stores only a backend-owned completed route, padding unspecified outputs with None.</summary>
    internal void RememberFramebufferDrawRouting(int framebuffer,ReadOnlySpan<DrawBuffersEnum> route)
    {
        var stored=new DrawBuffersEnum[Math.Max(route.Length, GpuSupport.HasCachedCapabilitiesForCurrentContext ? GpuSupport.Graphics.MaxDrawBuffers : 0)];
        route.CopyTo(stored);framebufferDrawRouting[framebuffer]=stored;
    }
    /// <summary>Withdraws routing for a retired name or an uncertain routing outcome without native calls.</summary>
    internal void InvalidateFramebufferDrawRouting(int framebuffer)=>framebufferDrawRouting.Remove(framebuffer);
    #endregion
}
