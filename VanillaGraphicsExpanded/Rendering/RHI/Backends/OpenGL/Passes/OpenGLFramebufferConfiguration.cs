using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes managed attachment composition and retains its command-established descriptors.</summary>
internal static class OpenGLFramebufferConfiguration
{
    #region Internal API
    /// <summary>Establishes a newly configured integration target route with core named commands and original-context publication.</summary>
    internal static void ConfigureRouting(GpuFramebuffer resource, DrawBuffersEnum[] route, ReadBufferMode read)
    {
        resource.RequireOriginalContext();
        OpenGLFramebufferRouting.Restore(OpenGlStateCache.Current, resource.FboId, route);
        OpenGLDriverCalls.NamedFramebufferReadBuffer(resource.FboId, (OpenTK.Graphics.OpenGL4.ReadBufferMode)read);
        resource.RequireOriginalContext();
        OpenGlStateCache.Current.RememberFramebufferReadRouting(resource.FboId, read);
    }

    /// <summary>Binds a realized framebuffer, acquiring only unknown external descriptors.</summary>
    internal static void BindFramebufferResource(GpuFramebuffer resource, FramebufferTarget target)
    {
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, target, resource.FboId);
        if (resource.FboId != 0 && !resource.HasNativeMetadata)
            RHIResourceManager.AcquireWrapperMetadata(resource, GpuFramebufferMetadataReader.Instance);
    }

    /// <summary>Reattaches selected images as one coherent metadata publication.</summary>
    internal static void ReattachFramebuffer(GpuFramebuffer resource, IEnumerable<KeyValuePair<FramebufferSlot, GpuFramebufferAttachment>> attachments)
    {
        using var operation = new FramebufferOperation(resource);
        foreach (var pair in attachments) FramebufferTexture(resource, pair.Key, pair.Value, operation);
        operation.Commit();
    }

    /// <summary>Owns standalone attachment publication or contributes to an explicitly supplied composition.</summary>
    internal static void FramebufferTexture(GpuFramebuffer resource, FramebufferSlot slot, GpuFramebufferAttachment? image,
        FramebufferOperation? operation = null)
    {
        OpenGlStateCache.Current.RequireMutableFramebufferConfiguration(resource.FboId);
        using var owned = operation is null ? new FramebufferOperation(resource) : null;
        var active = operation ?? owned!;
        try
        {
            active.RequireParticipation(resource);
            image?.ValidateSlot(slot);
            if (image is { ImageTarget: TextureDimension.TwoArray, AttachmentMode: GpuFramebufferAttachmentMode.Unknown }
                && GpuSupport.Graphics.SupportsOvrMultiview)
                throw new NotSupportedException("Unknown native multiview attachment replay semantics.");

            DispatchFramebufferAttachment(resource.FboId, OpenGLResourceValues.Native(slot), image);
            active.SetAttachment(slot, image);
            if (owned is not null) owned.Commit();
        }
        catch { active.Interrupt(); throw; }
    }

    #endregion

    #region Private
    /// <summary>Executes named attachment configuration without querying submitted facts or changing selectors.</summary>
    private static void DispatchFramebufferAttachment(int id, FramebufferAttachment slot, GpuFramebufferAttachment? image)
    {
        var origin = GpuResource.CaptureCreationContext(GpuResourceKind.Framebuffer);
        if (image is { AttachmentMode: GpuFramebufferAttachmentMode.Multiview })
        {
            if (!GpuSupport.Graphics.SupportsOvrMultiview) throw new NotSupportedException("OVR multiview is unavailable.");
            GL.Ovr.NamedFramebufferTextureMultiview(id, slot, image.TextureId, image.MipLevel, image.BaseView, image.ViewCount);
        }
        else if (image is { Kind: GpuResourceKind.Renderbuffer })
            OpenGLDriverCalls.NamedFramebufferRenderbuffer(id, (OpenTK.Graphics.OpenGL4.FramebufferAttachment)slot,
                OpenTK.Graphics.OpenGL4.RenderbufferTarget.Renderbuffer, image.RenderbufferId);
        else if (image?.Layer is not null || image?.CubeFace is not null)
            OpenGLDriverCalls.NamedFramebufferTextureLayer(id, (OpenTK.Graphics.OpenGL4.FramebufferAttachment)slot,
                image.TextureId, image.MipLevel, image.Layer ?? ((int)image.CubeFace!.Value - (int)CubeFace.PositiveX));
        else OpenGLDriverCalls.NamedFramebufferTexture(id, (OpenTK.Graphics.OpenGL4.FramebufferAttachment)slot,
            image?.TextureId ?? 0, image?.MipLevel ?? 0);
        if (!VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) throw new InvalidOperationException("Attachment configuration returned in a foreign context.");
    }
    #endregion

    /// <summary>Owns an explicit framebuffer candidate and its original-identity transaction.</summary>
    internal sealed class FramebufferOperation : IDisposable
    {
        private readonly RHIResourceManager.MetadataOperation<GpuFramebufferMetadata> operation;
        private GpuFramebufferMetadata candidate;

        #region Internal API
        /// <summary>Captures available facts before withdrawing the operation's snapshot.</summary>
        internal FramebufferOperation(GpuFramebuffer resource)
        {
            if (!resource.HasNativeMetadata || resource.ResourceManager?.GetResourceRegistration(resource) is { MetadataAvailable: false })
                RHIResourceManager.AcquireWrapperMetadata(resource, GpuFramebufferMetadataReader.Instance);
            candidate = resource.MetadataSnapshot;
            operation = new(resource, GpuMetadataChange.Metadata);
        }
        /// <summary>Checks explicit participation before a command executes.</summary>
        internal void RequireParticipation(GpuFramebuffer resource) => operation.RequireParticipation(resource, GpuMetadataChange.Metadata);
        /// <summary>Prevents a caught command failure from publishing a partial result.</summary>
        internal void Interrupt() => operation.Interrupt();
        /// <summary>Updates only the slots affected by the completed command.</summary>
        internal void SetAttachment(FramebufferSlot slot, GpuFramebufferAttachment? image)
            => candidate = OpenGlStateCache.WithFramebufferAttachment(candidate, slot, image);
        /// <summary>Commits the complete candidate without native rediscovery.</summary>
        internal void Commit() => operation.Commit(candidate);
        #endregion
        #region Public API
        /// <summary>Releases execution bookkeeping without treating disposal as success.</summary>
        public void Dispose() => operation.Dispose();
        #endregion
    }

}
