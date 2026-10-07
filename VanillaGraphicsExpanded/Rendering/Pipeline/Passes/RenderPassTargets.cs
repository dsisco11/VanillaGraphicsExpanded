using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Validates concrete image metadata and borrows its owners for one immediate pass.</summary>
internal sealed class RenderPassTargets : IDisposable
{
    private readonly GpuFramebuffer target;
    private readonly ulong revision;
    private readonly List<GpuResource> borrowed = new();
    internal RenderTargetSignature Signature { get; }
    internal RenderArea Area { get; }

    #region Public API
    /// <summary>Resolves exact formats, samples and area before any target mutation or clear.</summary>
    internal RenderPassTargets(RenderPassDesc description)
    {
        target = description.Target;
        target.ValidateAttachments();
        if (target.FboId == 0)
        {
            var surface = target.Surface ?? throw new NotSupportedException("Strict passes require published window-surface metadata.");
            if (description.Colors.Count != 1 || description.Colors[0].SurfaceBuffer != surface.Buffer)
                throw new ArgumentException("Window passes require their published front/back color route.");
            description.Colors[0].Clear?.Validate(surface.Color);
            Signature = new([new(surface.Color)], surface.DepthStencil, surface.Samples, surface.HasDepth, surface.HasStencil);
            Area = description.Area ?? new(0, 0, target.Width, target.Height);
            if (Area.X < 0 || Area.Y < 0 || Area.Width <= 0 || Area.Height <= 0
                || (long)Area.X + Area.Width > target.Width || (long)Area.Y + Area.Height > target.Height)
                throw new ArgumentOutOfRangeException(nameof(description));
            if ((!surface.HasDepth && (description.DepthStencil.DepthLoad != AttachmentLoad.Preserve || description.DepthStencil.DepthStore != AttachmentStore.Preserve))
                || (!surface.HasStencil && (description.DepthStencil.StencilLoad != AttachmentLoad.Preserve || description.DepthStencil.StencilStore != AttachmentStore.Preserve)))
                throw new ArgumentException("Window attachment intentions require a published aspect.");
            revision = target.AttachmentRevision;
            Borrow(target);
            return;
        }
        foreach (var color in description.Colors)
            if (color.SurfaceBuffer is not null) throw new ArgumentException("Image framebuffers cannot use window buffer routes.");
        if (!target.HasRenderPassMetadata)
            throw new InvalidOperationException("Publish complete wrapped framebuffer metadata before beginning a pass.");
        int width = 0, height = 0, samples = -1, count = 0;
        foreach (var pair in target.AttachmentImages)
        {
            var image = pair.Value;
            if (pair.Key >= FramebufferAttachment.ColorAttachment0 && pair.Key <= FramebufferAttachment.ColorAttachment15
                && !TargetFormatPolicy.IsColor(image.InternalFormat))
                throw new InvalidOperationException("Strict pass metadata requires a supported sized color format.");
            if (count++ == 0) { width = image.Width; height = image.Height; samples = Math.Max(1, image.Samples); }
            if (image.Width != width || image.Height != height || Math.Max(1, image.Samples) != samples)
                throw new InvalidOperationException("Pass images require equal dimensions and sample counts.");
        }
        if (count == 0 || width <= 0 || height <= 0)
            throw new InvalidOperationException("Target metadata is absent; publish wrapped framebuffer attachments first.");
        if (description.Colors.Count > GpuSupport.Graphics.MaxDrawBuffers)
            throw new ArgumentException("Too many draw outputs.");
        var colors = new List<ColorTargetSlot>();
        foreach (var color in description.Colors)
        {
            if (color.Attachment < 0) { colors.Add(new(null, color.DiscardOutput)); continue; }
            var image = target.GetAttachment(FramebufferAttachment.ColorAttachment0 + color.Attachment)
                ?? throw new InvalidOperationException("A routed color attachment is missing.");
            colors.Add(new(image.InternalFormat));
            color.Clear?.Validate(image.InternalFormat);
        }
        var depth = target.GetAttachment(FramebufferAttachment.DepthAttachment);
        var stencil = target.GetAttachment(FramebufferAttachment.StencilAttachment);
        if (depth is not null && stencil is not null && (depth.ResourceId != stencil.ResourceId
            || depth.TextureId != stencil.TextureId || depth.MipLevel != stencil.MipLevel
            || depth.Layer != stencil.Layer || depth.CubeFace != stencil.CubeFace))
            throw new NotSupportedException("Separate depth and stencil storage requires a separate target signature policy.");
        Signature = new(colors, depth?.InternalFormat ?? stencil?.InternalFormat, samples, depth is not null, stencil is not null);
        var intentions = description.DepthStencil;
        if ((depth is null && (intentions.DepthLoad != AttachmentLoad.Preserve || intentions.DepthStore != AttachmentStore.Preserve))
            || (stencil is null && (intentions.StencilLoad != AttachmentLoad.Preserve || intentions.StencilStore != AttachmentStore.Preserve)))
            throw new ArgumentException("An attachment intention requires the corresponding aspect.");
        Area = description.Area ?? new(0, 0, width, height);
        if (Area.X < 0 || Area.Y < 0 || Area.Width <= 0 || Area.Height <= 0
            || (long)Area.X + Area.Width > width || (long)Area.Y + Area.Height > height)
            throw new ArgumentOutOfRangeException(nameof(description), "Render area exceeds the current target dimensions.");
        revision = target.AttachmentRevision;
        try
        {
            Borrow(target);
            foreach (var pair in target.AttachmentImages)
            {
                Borrow(pair.Value);
                if (pair.Value.Resource is { } storage) Borrow(storage);
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Rejects obsolete or retired borrowed targets without querying native state.</summary>
    internal void Validate()
    {
        if (target.IsDisposed || target.AttachmentRevision != revision)
            throw new InvalidOperationException("Render target changed during an active pass; end and begin again.");
        target.ValidateAttachments();
    }

    /// <summary>Releases pass references while preserving every resource's original owner.</summary>
    public void Dispose()
    {
        foreach (var resource in borrowed) resource.ReleasePassReference();
        borrowed.Clear();
    }
    #endregion

    #region Private
    /// <summary>Retains shared backing resources only once, including packed depth/stencil aliases.</summary>
    private void Borrow(GpuResource resource)
    {
        if (borrowed.Contains(resource)) return;
        resource.RetainPassReference();
        borrowed.Add(resource);
    }
    #endregion
}
