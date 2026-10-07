using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Freezes routing and attachment intentions while borrowing an existing framebuffer.</summary>
internal sealed class RenderPassDesc
{
    internal GpuFramebuffer Target { get; }
    internal PipelineValues<RenderPassColor> Colors { get; }
    internal RenderPassDepthStencil DepthStencil { get; }
    internal RenderArea? Area { get; }

    #region Public API
    /// <summary>Copies output routing; an omitted area resolves to the current full target on each begin.</summary>
    internal RenderPassDesc(GpuFramebuffer target, IEnumerable<RenderPassColor> colors,
        RenderPassDepthStencil? depthStencil = null, RenderArea? area = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(colors);
        Target = target; Colors = new(colors); DepthStencil = depthStencil ?? new(); Area = area;
        var used = new HashSet<int>();
        foreach (var color in Colors)
        {
            ArgumentNullException.ThrowIfNull(color);
            if (color.Attachment < -1 || color.Attachment > 15 || !Enum.IsDefined(color.Load) || !Enum.IsDefined(color.Store))
                throw new ArgumentException("Invalid color attachment intention.", nameof(colors));
            if (color.Attachment >= 0 && (!used.Add(color.Attachment) || color.DiscardOutput))
                throw new ArgumentException("Attachments cannot receive duplicate outputs or routed discard policies.", nameof(colors));
            if (color.Attachment < 0 && (color.Load != AttachmentLoad.Preserve || color.Store != AttachmentStore.Preserve || color.Clear is not null))
                throw new ArgumentException("Unrouted outputs cannot perform attachment operations.", nameof(colors));
            if ((color.Load == AttachmentLoad.Clear) != (color.Clear is not null))
                throw new ArgumentException("Only clear loads require a typed clear value.", nameof(colors));
        }
        if (!Enum.IsDefined(DepthStencil.DepthLoad) || !Enum.IsDefined(DepthStencil.StencilLoad)
            || !Enum.IsDefined(DepthStencil.DepthStore) || !Enum.IsDefined(DepthStencil.StencilStore)
            || !float.IsFinite(DepthStencil.ClearDepth) || DepthStencil.ClearDepth < 0 || DepthStencil.ClearDepth > 1)
            throw new ArgumentException("Invalid depth/stencil intention.", nameof(depthStencil));
    }
    #endregion
}
