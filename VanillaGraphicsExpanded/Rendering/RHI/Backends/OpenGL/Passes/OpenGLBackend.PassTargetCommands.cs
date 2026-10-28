using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes named target configuration and selector changes after canonical input validation.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Configures the completed candidate's fixed output route and proves completeness before any pass loads.</summary>
    internal void CompletePassTarget(OpenGLPassTarget target, Legacy.DrawBuffersEnum[] route)
    {
        RequireContext();
        var outputs = new DrawBuffersEnum[Math.Max(1, route.Length)];
        for (int i = 0; i < route.Length; i++) outputs[i] = (DrawBuffersEnum)route[i];
        OpenGLDriverCalls.NamedFramebufferDrawBuffers(target.FboId, outputs.Length, outputs);
        int first = Array.FindIndex(route, value => value != Legacy.DrawBuffersEnum.None);
        OpenGLDriverCalls.NamedFramebufferReadBuffer(target.FboId, first < 0 ? ReadBufferMode.None : (ReadBufferMode)route[first]);
        RequireContext();
        var status = GL.CheckNamedFramebufferStatus(target.FboId, FramebufferTarget.Framebuffer);
        RequireContext();
        if (status != FramebufferStatus.FramebufferComplete)
            throw new InvalidOperationException($"Framebuffer realization is incomplete: {status}.");
    }
    /// <summary>Selects only a surviving completed draw target and publishes selector knowledge after native return.</summary>
    internal void SelectPassTarget(GpuFramebuffer target)
    {
        RequireContext();
        if (cache.TryGetCachedCurrentFramebuffer(Legacy.FramebufferTarget.DrawFramebuffer, out int current) && current == target.FboId) return;
        cache.SetFramebufferSelectionKnowledge(Legacy.FramebufferTarget.DrawFramebuffer, null);
        OpenGLDriverCalls.BindFramebuffer(FramebufferTarget.DrawFramebuffer, target.FboId);
        RequireContext();
        cache.SetFramebufferSelectionKnowledge(Legacy.FramebufferTarget.DrawFramebuffer, target.FboId);
    }
    #endregion
}
