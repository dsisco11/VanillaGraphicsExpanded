using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns validated attachment configuration for construction and mutable framebuffer implementations.</summary>
public abstract partial class GpuFramebuffer
{
    #region Protected API
    /// <summary>Attaches borrowed images and establishes routing, including leading holes and depth-only targets.</summary>
    private protected DrawBuffersEnum[] ConfigureAttachments(IReadOnlyList<GpuFramebufferAttachment> colors,
        GpuFramebufferAttachment? depth, int firstColorAttachment)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (firstColorAttachment < 0 || firstColorAttachment > 31 || colors.Count > 32 - firstColorAttachment)
            throw new ArgumentOutOfRangeException(nameof(firstColorAttachment));
        for (int i = 0; i < colors.Count; i++) SetAttachmentCore(ColorSlot(firstColorAttachment + i), colors[i]);
        if (depth is not null) SetAttachmentCore(depth.DepthStencilSlot, depth);
        // Preserve shader locations without allocating dummy storage for leading holes.
        var route = colors.Count == 0 ? Array.Empty<DrawBuffersEnum>()
            : Enumerable.Range(0, firstColorAttachment + colors.Count)
                .Select(i => i < firstColorAttachment ? DrawBuffersEnum.None : DrawBuffersEnum.ColorAttachment0 + i).ToArray();
        if (route.Length == 0)
        {
            RHI.RHICommandContext.Current.ConfigureFramebufferRouting(this, route, ReadBufferMode.None);
        }
        else
        {
            RHI.RHICommandContext.Current.ConfigureFramebufferRouting(this, route, ReadBufferMode.ColorAttachment0 + firstColorAttachment);
        }
        if (!CheckStatus(out var error)) throw new InvalidOperationException(error);
        return route;
    }
    /// <summary>Commits a validated attachment for mutable subclasses or construction before publication.</summary>
    private protected void SetAttachmentCore(FramebufferAttachment slot, GpuFramebufferAttachment attachment)
    {
        RHI.RHICommandContext.Current.RequireMutableFramebufferOwner(this);
        RequireMutableStorage();
        ArgumentNullException.ThrowIfNull(attachment);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (fboId == 0) throw new InvalidOperationException("The default framebuffer cannot accept attachments.");
        attachment.ValidateSlot(slot);
        // Repair only the requested slot. Rendering validation would reject an invalid outgoing
        // image before it can be replaced. Other pending replacements remain dirty until Bind.
        attachment.AttachTo(this, slot);
        // Packed attachment calls update two independent GL aspect slots. Keep both
        // references so replacing depth later does not lose the remaining stencil image.
        if (slot == FramebufferAttachment.DepthStencilAttachment)
        {
            attachments[FramebufferAttachment.DepthAttachment] = attachment;
            attachments[FramebufferAttachment.StencilAttachment] = attachment;
        }
        else attachments[slot] = attachment;
        attachment.Observe(this);
        PublishAttachmentsChanged();
    }

    /// <summary>Detaches mutable configuration without changing independently owned image lifetimes.</summary>
    private protected bool RemoveAttachmentCore(FramebufferAttachment slot)
    {
        RHI.RHICommandContext.Current.RequireMutableFramebufferOwner(this);
        RequireMutableStorage();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        bool present = slot == FramebufferAttachment.DepthStencilAttachment
            ? attachments.ContainsKey(FramebufferAttachment.DepthAttachment) || attachments.ContainsKey(FramebufferAttachment.StencilAttachment)
            : attachments.ContainsKey(slot);
        if (!present) return false;
        // Detachment must remain possible when the outgoing image is unavailable. Keep any
        // other pending replacements dirty so their native attachments refresh at rendering use.
        RHI.RHICommandContext.Current.ConfigureFramebufferAttachment(this, slot, null);
        if (slot == FramebufferAttachment.DepthStencilAttachment)
        {
            attachments.Remove(FramebufferAttachment.DepthAttachment);
            attachments.Remove(FramebufferAttachment.StencilAttachment);
        }
        else attachments.Remove(slot);
        PublishAttachmentsChanged();
        return true;
    }

    /// <summary>Refreshes mutable backing allocations while retaining the framebuffer identity.</summary>
    private protected bool ResizeCore(int newWidth, int newHeight)
    {
        RHI.RHICommandContext.Current.RequireMutableFramebufferOwner(this);
        RequireMutableStorage();
        if (!IsValid) return false;
        bool changed = false;
        var visited = new HashSet<GpuResource>(ReferenceEqualityComparer.Instance);
        resizing = true;
        try
        {
            // Multiple slots and image wrappers may share one allocation. Resize that
            // allocation once, while deferring this framebuffer's completion publication.
            foreach (var image in attachments.Values.Distinct())
                if (image.Resource is { } storage && visited.Add(storage)) changed |= image.Resize(newWidth, newHeight);
        }
        finally
        {
            resizing = false;
            if (changed) PublishAttachmentsChanged();
        }
        return changed;
    }
    #endregion
}
