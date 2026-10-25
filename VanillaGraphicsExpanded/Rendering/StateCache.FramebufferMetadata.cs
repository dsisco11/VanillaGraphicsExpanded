using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes attachment commands and publishes actual framebuffer descriptors independently of retained image policy.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    #region Framebuffer configuration
    /// <summary>Binds a managed framebuffer and acquires initial native facts after a generated name is realized.</summary>
    internal void BindFramebufferResource(GpuFramebuffer resource, FramebufferTarget target)
    {
        BindFramebuffer(target, resource.FboId);
        if (resource.FboId != 0 && !resource.HasNativeMetadata)
            GpuResourceManager.AcquireWrapperMetadata(resource, GpuFramebufferMetadataReader.Instance);
    }

    /// <summary>Reattaches the supplied images and publishes one completed framebuffer result.</summary>
    internal void ReattachFramebuffer(GpuFramebuffer resource, IEnumerable<KeyValuePair<FramebufferAttachment, GpuFramebufferAttachment>> attachments)
    {
        using var operation = new GpuResourceManager.MetadataOperation<GpuFramebufferMetadata>(resource, GpuMetadataChange.Metadata);
        foreach (var pair in attachments) pair.Value.AttachTo(resource, pair.Key, operation);
        operation.RequireLive();
        operation.Commit(GpuResourceManager.ReadFramebufferOperationResult(resource));
    }

    #endregion

    #region Native commands
    /// <summary>Attaches a proven multiview range through a capability-selected named command or bound fallback.</summary>
    internal void FramebufferTextureMultiview(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment image,
        GpuResourceManager.MetadataOperation<GpuFramebufferMetadata>? operation = null)
    {
        if (operation is null) RequireResourceOperationAvailable(resource.Kind, resource.ResourceId);
        else operation.RequireParticipation(resource, GpuMetadataChange.Metadata);
        try
        {
            if (!GpuSupport.Graphics.SupportsOvrMultiview) throw new NotSupportedException("OVR multiview is unavailable.");
            if (GpuSupport.Graphics.SupportsArbDirectStateAccess)
                GL.Ovr.NamedFramebufferTextureMultiview(resource.FboId, slot, image.TextureId, image.MipLevel, image.BaseView, image.ViewCount);
            else GL.Ovr.FramebufferTextureMultiview(FramebufferTarget.Framebuffer, slot, image.TextureId, image.MipLevel, image.BaseView, image.ViewCount);
            ObserveFramebufferAttachment(resource, slot, image, operation);
        }
        catch
        {
            operation?.Interrupt();
            throw;
        }
    }

    /// <summary>Attaches a renderbuffer image to the bound framebuffer.</summary>
    internal void FramebufferRenderbuffer(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment image,
        GpuResourceManager.MetadataOperation<GpuFramebufferMetadata>? operation = null)
    {
        if (operation is null) RequireResourceOperationAvailable(resource.Kind, resource.ResourceId);
        else operation.RequireParticipation(resource, GpuMetadataChange.Metadata);
        try
        {
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, slot, RenderbufferTarget.Renderbuffer, image.RenderbufferId);
            ObserveFramebufferAttachment(resource, slot, image, operation);
        }
        catch
        {
            operation?.Interrupt();
            throw;
        }
    }

    /// <summary>Attaches one selected texture layer to the bound framebuffer.</summary>
    internal void FramebufferTextureLayer(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment image,
        GpuResourceManager.MetadataOperation<GpuFramebufferMetadata>? operation = null)
    {
        if (operation is null) RequireResourceOperationAvailable(resource.Kind, resource.ResourceId);
        else operation.RequireParticipation(resource, GpuMetadataChange.Metadata);
        try
        {
            GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, slot, image.TextureId, image.MipLevel, image.Layer!.Value);
            ObserveFramebufferAttachment(resource, slot, image, operation);
        }
        catch
        {
            operation?.Interrupt();
            throw;
        }
    }

    /// <summary>Attaches a two-dimensional texture image or cube face to the bound framebuffer.</summary>
    internal void FramebufferTexture2D(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment image,
        GpuResourceManager.MetadataOperation<GpuFramebufferMetadata>? operation = null)
    {
        if (operation is null) RequireResourceOperationAvailable(resource.Kind, resource.ResourceId);
        else operation.RequireParticipation(resource, GpuMetadataChange.Metadata);
        try
        {
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, slot, image.ImageTarget, image.TextureId, image.MipLevel);
            ObserveFramebufferAttachment(resource, slot, image, operation);
        }
        catch
        {
            operation?.Interrupt();
            throw;
        }
    }

    /// <summary>Attaches a complete texture level, or detaches the slot when no image is supplied.</summary>
    internal void FramebufferTexture(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment? image,
        GpuResourceManager.MetadataOperation<GpuFramebufferMetadata>? operation = null)
    {
        if (operation is null) RequireResourceOperationAvailable(resource.Kind, resource.ResourceId);
        else operation.RequireParticipation(resource, GpuMetadataChange.Metadata);
        try
        {
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, slot, image?.TextureId ?? 0, image?.MipLevel ?? 0);
            ObserveFramebufferAttachment(resource, slot, image, operation);
        }
        catch
        {
            operation?.Interrupt();
            throw;
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Validates the native result and publishes only for an independent attachment operation.</summary>
    private void ObserveFramebufferAttachment(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment? image,
        GpuResourceManager.MetadataOperation<GpuFramebufferMetadata>? operation = null)
    {
        RequireNativeAttachment(resource, slot, image);
        if (image is { Kind: GpuResourceKind.Texture, ImageTarget: TextureTarget.Texture2DArray }
            && resource.ResourceManager?.GetResourceRegistration(resource) is GpuResourceMetadataEntry<GpuFramebufferMetadata> entry)
            entry.Manager.RecordFramebufferAttachmentCommand(entry, new(slot, image.TextureId, image.MipLevel,
                image.Layer ?? -1, image.BaseView, image.ViewCount, image.AttachmentMode, true, false));
        if (operation is null) GpuResourceManager.ObserveWrapperMetadata(resource, GpuFramebufferMetadataReader.Instance, GpuMetadataChange.Metadata);
    }

    /// <summary>Checks the actual bound attachment without consuming native errors or certifying a rejected command.</summary>
    private void RequireNativeAttachment(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment? expected)
    {
        if (slot == FramebufferAttachment.DepthStencilAttachment)
        {
            RequireNativeAttachment(resource, FramebufferAttachment.DepthAttachment, expected);
            RequireNativeAttachment(resource, FramebufferAttachment.StencilAttachment, expected);
            return;
        }
        // Invalid sentinels prevent an unsupported query from masquerading as a committed descriptor.
        int Read(FramebufferParameterName parameter)
        {
            int value = -1;
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, slot, parameter, out value);
            return value;
        }
        int kind = Read(FramebufferParameterName.FramebufferAttachmentObjectType);
        int name = Read(FramebufferParameterName.FramebufferAttachmentObjectName);
        bool matches = expected is null ? kind == (int)All.None && name == 0
            : name == expected.ResourceId && kind == (int)(expected.Kind == GpuResourceKind.Renderbuffer ? All.Renderbuffer : All.Texture);
        if (matches && expected is { Kind: GpuResourceKind.Texture })
            matches = Read(FramebufferParameterName.FramebufferAttachmentTextureLevel) == expected.MipLevel
                && Read(FramebufferParameterName.FramebufferAttachmentTextureCubeMapFace) == (int)(expected.CubeFace ?? 0)
                && (expected.AttachmentMode == GpuFramebufferAttachmentMode.Multiview
                    ? Read((FramebufferParameterName)All.FramebufferAttachmentTextureBaseViewIndexOvr) == expected.BaseView && Read((FramebufferParameterName)All.FramebufferAttachmentTextureNumViewsOvr) == expected.ViewCount
                    : Read(FramebufferParameterName.FramebufferAttachmentLayered) == (expected.MetadataImageSelection.Layered ? 1 : 0))
                && (!expected.Layer.HasValue || Read(FramebufferParameterName.FramebufferAttachmentTextureLayer) == expected.Layer.Value);
        if (matches) return;
        GpuResourceManager.InvalidateWrapperMetadata(resource);
        throw new System.InvalidOperationException("The native framebuffer attachment command was rejected or could not be verified.");
    }

    #endregion
}
