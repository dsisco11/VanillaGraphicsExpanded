using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes attachment commands and publishes actual framebuffer descriptors independently of retained image policy.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    #region Update groups
    /// <summary>Binds a managed framebuffer and acquires initial native facts after a generated name is realized.</summary>
    internal void BindFramebufferResource(GpuFramebuffer resource, FramebufferTarget target)
    {
        BindFramebuffer(target, resource.FboId);
        if (resource.FboId != 0 && !resource.HasNativeMetadata)
            GpuResourceManager.AcquireWrapperMetadata(resource, GpuFramebufferMetadataReader.Instance);
    }

    /// <summary>Defers publication while the framebuffer owner reattaches all pending image replacements.</summary>
    internal MetadataUpdate<GpuFramebufferMetadata> BeginFramebufferAttachmentUpdate(GpuFramebuffer resource)
        => new(this, resource, GpuFramebufferMetadataReader.Instance, GpuMetadataChange.Metadata);

    #endregion

    #region Native commands
    /// <summary>Attaches a renderbuffer image to the bound framebuffer.</summary>
    internal void FramebufferRenderbuffer(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment image)
    {
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, slot, RenderbufferTarget.Renderbuffer, image.RenderbufferId);
        ObserveFramebufferAttachment(resource, slot, image);
    }

    /// <summary>Attaches one selected texture layer to the bound framebuffer.</summary>
    internal void FramebufferTextureLayer(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment image)
    {
        GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, slot, image.TextureId, image.MipLevel, image.Layer!.Value);
        ObserveFramebufferAttachment(resource, slot, image);
    }

    /// <summary>Attaches a two-dimensional texture image or cube face to the bound framebuffer.</summary>
    internal void FramebufferTexture2D(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment image)
    {
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, slot, image.ImageTarget, image.TextureId, image.MipLevel);
        ObserveFramebufferAttachment(resource, slot, image);
    }

    /// <summary>Attaches a complete texture level, or detaches the slot when no image is supplied.</summary>
    internal void FramebufferTexture(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment? image)
    {
        GL.FramebufferTexture(FramebufferTarget.Framebuffer, slot, image?.TextureId ?? 0, image?.MipLevel ?? 0);
        ObserveFramebufferAttachment(resource, slot, image);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Validates the native result and publishes immediately unless an attachment group is still active.</summary>
    private void ObserveFramebufferAttachment(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment? image)
    {
        RequireNativeAttachment(resource, slot, image);
        ObserveMetadata(resource, GpuFramebufferMetadataReader.Instance, GpuMetadataChange.Metadata);
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
                && Read(FramebufferParameterName.FramebufferAttachmentLayered) == (expected.MetadataImageSelection.Layered ? 1 : 0)
                && (!expected.Layer.HasValue || Read(FramebufferParameterName.FramebufferAttachmentTextureLayer) == expected.Layer.Value);
        if (matches) return;
        GpuResourceManager.InvalidateWrapperMetadata(resource);
        throw new System.InvalidOperationException("The native framebuffer attachment command was rejected or could not be verified.");
    }

    #endregion
}
