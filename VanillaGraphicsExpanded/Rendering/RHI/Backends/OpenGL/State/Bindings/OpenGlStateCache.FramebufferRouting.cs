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
    /// <summary>Returns a private snapshot of established routing, including immutable configuration declarations.</summary>
    internal bool TryCopyFramebufferDrawRouting(int framebuffer,out DrawBuffersEnum[] route)
    {
        if(TryGetImmutableFramebuffer(framebuffer,out var immutable))
        { route=System.Array.ConvertAll(immutable.CaptureDeclaredRouting(), value => value == -1 ? DrawBuffersEnum.None : DrawBuffersEnum.ColorAttachment0 + value);return true; }
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
    /// <summary>Certifies an immutable declaration or leaves mutable native routing to its backend owner.</summary>
    internal bool ValidateImmutableFramebufferRouting(int framebuffer,ReadOnlySpan<DrawBuffersEnum> route)
    {
        if(!TryGetImmutableFramebuffer(framebuffer,out var immutable))return false;
        immutable.ValidateDeclaredRouting(route.ToArray().Select(value => value == DrawBuffersEnum.None ? -1 : (int)value - (int)DrawBuffersEnum.ColorAttachment0).ToArray());return true;
    }
    /// <summary>Stores only a backend-owned completed route, padding unspecified outputs with None.</summary>
    internal void RememberFramebufferDrawRouting(int framebuffer,ReadOnlySpan<DrawBuffersEnum> route)
    {
        var stored=new DrawBuffersEnum[GpuSupport.Graphics.MaxDrawBuffers];
        route.CopyTo(stored);framebufferDrawRouting[framebuffer]=stored;
    }
    /// <summary>Withdraws routing for a retired, transferred or externally changed framebuffer without native calls.</summary>
    internal void InvalidateFramebufferDrawRouting(int framebuffer)=>framebufferDrawRouting.Remove(framebuffer);
    #endregion
}
