using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes managed attachment composition and retains its command-established descriptors.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Binds a realized framebuffer, acquiring only unknown external descriptors.</summary>
    internal void BindFramebufferResource(GpuFramebuffer resource, FramebufferTarget target)
    {
        BindFramebuffer(target, resource.FboId);
        if (resource.FboId != 0 && !resource.HasNativeMetadata)
            GpuResourceManager.AcquireWrapperMetadata(resource, GpuFramebufferMetadataReader.Instance);
    }

    /// <summary>Reattaches selected images as one coherent metadata publication.</summary>
    internal void ReattachFramebuffer(GpuFramebuffer resource, IEnumerable<KeyValuePair<FramebufferAttachment, GpuFramebufferAttachment>> attachments)
    {
        using var operation = new FramebufferOperation(resource);
        foreach (var pair in attachments) pair.Value.AttachTo(resource, pair.Key, operation);
        operation.Commit();
    }

    /// <summary>Owns standalone attachment publication or contributes to an explicitly supplied composition.</summary>
    internal void FramebufferTexture(GpuFramebuffer resource, FramebufferAttachment slot, GpuFramebufferAttachment? image,
        FramebufferOperation? operation = null)
    {
        RequireMutableFramebufferConfiguration(resource.FboId);
        using var owned = operation is null ? new FramebufferOperation(resource) : null;
        var active = operation ?? owned!;
        try
        {
            active.RequireParticipation(resource);
            DispatchFramebufferAttachment(resource.FboId, slot, image);
            active.SetAttachment(slot, image);
            if (owned is not null) owned.Commit();
        }
        catch { active.Interrupt(); throw; }
    }

    #endregion

    #region Private
    /// <summary>Selects named or binding-preserving dispatch without querying the submitted descriptor.</summary>
    private void DispatchFramebufferAttachment(int id, FramebufferAttachment slot, GpuFramebufferAttachment? image)
    {
        bool named = GpuSupport.Graphics.SupportsArbDirectStateAccess;
        using var binding = named ? (FramebufferScope?)null : BindFramebufferScope(FramebufferTarget.Framebuffer, id);
        if (image is { AttachmentMode: GpuFramebufferAttachmentMode.Multiview })
        {
            if (!GpuSupport.Graphics.SupportsOvrMultiview) throw new NotSupportedException("OVR multiview is unavailable.");
            if (named) GL.Ovr.NamedFramebufferTextureMultiview(id, slot, image.TextureId, image.MipLevel, image.BaseView, image.ViewCount);
            else GL.Ovr.FramebufferTextureMultiview(FramebufferTarget.Framebuffer, slot, image.TextureId, image.MipLevel, image.BaseView, image.ViewCount);
        }
        else if (image is { Kind: GpuResourceKind.Renderbuffer })
        {
            if (named) GL.NamedFramebufferRenderbuffer(id, slot, RenderbufferTarget.Renderbuffer, image.RenderbufferId);
            else GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, slot, RenderbufferTarget.Renderbuffer, image.RenderbufferId);
        }
        else if (image?.Layer is int layer || image?.CubeFace is not null && named)
        {
            int selection = image!.Layer ?? ((int)image.CubeFace!.Value - (int)TextureTarget.TextureCubeMapPositiveX);
            if (named) GL.NamedFramebufferTextureLayer(id, slot, image.TextureId, image.MipLevel, selection);
            else GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, slot, image.TextureId, image.MipLevel, selection);
        }
        else if (!named && image is not null && (image.ImageTarget is TextureTarget.Texture2D or TextureTarget.TextureRectangle || image.CubeFace.HasValue))
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, slot, image.ImageTarget, image.TextureId, image.MipLevel);
        else if (named) GL.NamedFramebufferTexture(id, slot, image?.TextureId ?? 0, image?.MipLevel ?? 0);
        else GL.FramebufferTexture(FramebufferTarget.Framebuffer, slot, image?.TextureId ?? 0, image?.MipLevel ?? 0);
    }
    #endregion

    /// <summary>Owns an explicit framebuffer candidate and its original-identity transaction.</summary>
    internal sealed class FramebufferOperation : IDisposable
    {
        private readonly GpuResourceManager.MetadataOperation<GpuFramebufferMetadata> operation;
        private GpuFramebufferMetadata candidate;

        #region Internal API
        /// <summary>Captures available facts before withdrawing the operation's snapshot.</summary>
        internal FramebufferOperation(GpuFramebuffer resource)
        {
            if (!resource.HasNativeMetadata || resource.ResourceManager?.GetResourceRegistration(resource) is { MetadataAvailable: false })
                GpuResourceManager.AcquireWrapperMetadata(resource, GpuFramebufferMetadataReader.Instance);
            candidate = resource.MetadataSnapshot;
            operation = new(resource, GpuMetadataChange.Metadata);
        }
        /// <summary>Checks explicit participation before a command executes.</summary>
        internal void RequireParticipation(GpuFramebuffer resource) => operation.RequireParticipation(resource, GpuMetadataChange.Metadata);
        /// <summary>Prevents a caught command failure from publishing a partial result.</summary>
        internal void Interrupt() => operation.Interrupt();
        /// <summary>Updates only the slots affected by the completed command.</summary>
        internal void SetAttachment(FramebufferAttachment slot, GpuFramebufferAttachment? image)
            => candidate = WithFramebufferAttachment(candidate, slot, image);
        /// <summary>Commits the complete candidate without native rediscovery.</summary>
        internal void Commit() => operation.Commit(candidate);
        #endregion
        #region Public API
        /// <summary>Releases execution bookkeeping without treating disposal as success.</summary>
        public void Dispose() => operation.Dispose();
        #endregion
    }

    #region Private
    /// <summary>Constructs canonical selections centrally, including packed depth/stencil expansion.</summary>
    private static GpuFramebufferMetadata WithFramebufferAttachment(GpuFramebufferMetadata candidate,
        FramebufferAttachment slot, GpuFramebufferAttachment? image)
    {
        if (slot == FramebufferAttachment.DepthStencilAttachment)
            return WithFramebufferAttachment(WithFramebufferAttachment(candidate, FramebufferAttachment.DepthAttachment, image), FramebufferAttachment.StencilAttachment, image);
        var selections = candidate.Attachments;
        if (image is null) return candidate with { Attachments = selections.Remove(slot) };
        var descriptor = new GpuFramebufferImageMetadata(image.Kind, image.ResourceId, image.MipLevel,
            image.Layer ?? 0, image.CubeFace ?? 0, image.MetadataImageSelection.Layered,
            image.BaseView, image.ViewCount, image.AttachmentMode);
        return candidate with { Attachments = selections.SetItem(slot, descriptor) };
    }
    #endregion
}
