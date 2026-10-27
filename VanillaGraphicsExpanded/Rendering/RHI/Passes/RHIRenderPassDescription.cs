using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Freezes attachment-based or explicitly external pass intentions without realizing a native target.</summary>
public sealed class RHIRenderPassDescription
{
    #region Public API
    /// <summary>Gets fragment outputs in authored shader-location order, including sparse holes.</summary>
    public ImmutableArray<RHIColorAttachment> Colors { get; }
    /// <summary>Gets optional owned-view depth/stencil storage; actual aspects are validated during execution.</summary>
    public RHIAttachment? DepthStencil { get; }
    /// <summary>Gets independent depth and stencil load/store intentions.</summary>
    public RHIDepthStencilActions Actions { get; }
    /// <summary>Gets an optional supplied pixel area; omission selects the realized target's full dimensions.</summary>
    public RenderArea? Area { get; }
    /// <summary>Gets an explicit integration-owned surface instead of attachment-based realization.</summary>
    public RHISurfaceRef Surface { get; }
    /// <summary>Gets a diagnostic label excluded from target realization identity.</summary>
    public string? Label { get; }

    /// <summary>Copies supplied texture/renderbuffer intentions without reading metadata, allocating targets or requiring a context.</summary>
    public RHIRenderPassDescription(IEnumerable<RHIColorAttachment> colors, RHIAttachment? depthStencil = null,
        RHIDepthStencilActions? actions = null, RenderArea? area = null, string? label = null)
        : this(default, colors, depthStencil, actions, area, label) { }

    /// <summary>Copies explicit external output routing without borrowing native authority until pass execution.</summary>
    public RHIRenderPassDescription(RHISurfaceRef surface, IEnumerable<RHIColorAttachment> colors,
        RHIDepthStencilActions? actions = null, RenderArea? area = null, string? label = null)
        : this(surface, colors, null, actions, area, label)
    {
        if (surface.IsEmpty) throw new ArgumentException("An explicit surface reference is required.", nameof(surface));
    }
    #endregion

    #region Private
    /// <summary>Validates supplied shape and routing while leaving format, aspect, size and sample checks to execution.</summary>
    private RHIRenderPassDescription(RHISurfaceRef surface, IEnumerable<RHIColorAttachment> colors, RHIAttachment? depthStencil,
        RHIDepthStencilActions? actions, RenderArea? area, string? label)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Surface = surface; Colors = colors.ToImmutableArray(); DepthStencil = depthStencil;
        Actions = actions ?? new(); Area = area; Label = label;
        if (area is { } rectangle && (rectangle.X < 0 || rectangle.Y < 0 || rectangle.Width <= 0 || rectangle.Height <= 0))
            throw new ArgumentOutOfRangeException(nameof(area));
        if (depthStencil is { IsEmpty: true }) throw new ArgumentException("An empty depth/stencil selection is not an attachment.", nameof(depthStencil));
        var used = new HashSet<int>();
        bool hasWindowRoute = false;
        for (int output = 0; output < Colors.Length; output++)
        {
            var color = Colors[output] ?? throw new ArgumentException("Color intentions cannot contain null entries.", nameof(colors));
            if (!Enum.IsDefined(color.Load) || !Enum.IsDefined(color.Store)
                || color.WindowBuffer is { } window && !Enum.IsDefined(window)
                || color.AttachmentIndex is < 0 or > 15 || color.Image is { IsEmpty: true })
                throw new ArgumentException("Invalid supplied color intention.", nameof(colors));
            bool routed = color.Image.HasValue || color.AttachmentIndex.HasValue || color.WindowBuffer.HasValue;
            if (color.Image.HasValue && !surface.IsEmpty || !color.Image.HasValue && color.AttachmentIndex.HasValue && surface.IsEmpty
                || color.WindowBuffer.HasValue && (surface.IsEmpty || color.Image.HasValue || color.AttachmentIndex.HasValue)
                || color.DiscardOutput && routed || (color.Load == AttachmentLoad.Clear) != (color.Clear is not null)
                || !routed && (color.Load != AttachmentLoad.Preserve || color.Store != AttachmentStore.Preserve || color.Clear is not null))
                throw new ArgumentException("Color routing and attachment intentions disagree.", nameof(colors));
            if (color.WindowBuffer.HasValue)
            {
                if (hasWindowRoute || Colors.Length != 1) throw new ArgumentException("A window surface requires one explicit color output.", nameof(colors));
                hasWindowRoute = true;
            }
            if (color.Image.HasValue && (color.AttachmentIndex ?? output) > 15)
                throw new ArgumentException("The color attachment index exceeds the representable range.", nameof(colors));
            if (!color.WindowBuffer.HasValue && routed && !used.Add(color.AttachmentIndex ?? output))
                throw new ArgumentException("Multiple outputs cannot route to the same attachment.", nameof(colors));
        }
        if (!Enum.IsDefined(Actions.DepthLoad) || !Enum.IsDefined(Actions.DepthStore)
            || !Enum.IsDefined(Actions.StencilLoad) || !Enum.IsDefined(Actions.StencilStore)
            || !float.IsFinite(Actions.ClearDepth) || Actions.ClearDepth < 0 || Actions.ClearDepth > 1)
            throw new ArgumentException("Invalid depth/stencil intentions.", nameof(actions));
    }
    #endregion
}
