using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns mutable attachment configuration and cached, restorable framebuffer routing.</summary>
public sealed partial class DynamicGpuFramebuffer : GpuFramebuffer
{
    #region Public API
    /// <inheritdoc />
    public override void SetAttachment(FramebufferAttachment slot, GpuFramebufferAttachment attachment) => SetAttachmentCore(slot, attachment);
    /// <inheritdoc />
    public override bool RemoveAttachment(FramebufferAttachment slot) => RemoveAttachmentCore(slot);
    /// <inheritdoc />
    public override bool Resize(int width, int height) => ResizeCore(width, height);
    #endregion

    #region Internal API
    /// <summary>Allocates an owned identity or wraps a borrowed engine/window framebuffer.</summary>
    internal DynamicGpuFramebuffer(bool owns, int id = 0, string? name = null, int width = 0, int height = 0)
        : base(owns, id, name, width, height) { }
    /// <summary>Dynamic targets accept any route satisfying the common pass declaration contract.</summary>
    internal override void ValidatePassRouting(IReadOnlyList<DrawBuffersEnum> routing) { }
    /// <summary>Uses sequence restoration or an independent scope for mutable target routing.</summary>
    internal override IDisposable? BeginPassRouting(RenderPassDesc description, RenderPassSequence? sequence)
    {
        var routing = description.Routing.ToArray();
        if (sequence is not null) { sequence.Bind(this, routing); return null; }
        return new DynamicFramebufferPassRouting(this, routing);
    }
    /// <summary>Delegates live routing comparisons and DSA/fallback publication to the shared state owner.</summary>
    internal override void RestoreDrawRouting(DrawBuffersEnum[] routing)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        StateCache.Current.RestoreFramebufferDrawRouting(FboId, routing);
    }
    /// <summary>Checks changed attachments and routes while retaining dynamic target mutation support.</summary>
    internal override void ValidatePassCompleteness(IReadOnlyList<DrawBuffersEnum> routing) => ValidatePassCompletenessCore(routing);
    #endregion
}
