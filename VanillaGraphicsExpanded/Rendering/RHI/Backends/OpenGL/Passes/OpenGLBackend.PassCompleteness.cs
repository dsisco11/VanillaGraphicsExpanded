using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns realization reuse and revision-keyed completeness checks for selected native pass targets.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly ConditionalWeakTable<GpuFramebuffer, PassCompleteness> passCompleteness = new();
    /// <summary>Retains only the successful target revision and routing, never another resource registration.</summary>
    private sealed class PassCompleteness
    {
        internal ulong Revision;
        internal DrawBuffersEnum[] Routing = [];
    }
    #region Internal API
    /// <summary>Checks the selected draw target only when its attachment or routing contract changes.</summary>
    internal void RequirePassCompleteness(GpuFramebuffer target, IReadOnlyList<DrawBuffersEnum> routing)
    {
        RequireContext();
        if (target is OpenGLPassTarget realized)
        {
            if (!realized.IsReusable) throw new InvalidOperationException("The backend target realization is obsolete.");
            return;
        }
        if (target.FboId == 0 && target.Surface is not null) return;
        if (cache.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer) != target.FboId)
            throw new InvalidOperationException("Pass completeness requires this framebuffer's draw binding.");
        if (passCompleteness.TryGetValue(target, out var known) && known.Revision == target.AttachmentRevision
            && known.Routing.SequenceEqual(routing)) return;
        OpenGLFramebufferCompleteness.RequireComplete(target.FboId);
        passCompleteness.Remove(target);
        passCompleteness.Add(target, new() { Revision = target.AttachmentRevision, Routing = routing.ToArray() });
    }
    #endregion
}
