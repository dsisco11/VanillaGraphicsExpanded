using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Captures original storage association and selected configuration facts, never native-name identity alone.</summary>
internal readonly record struct OpenGLAttachmentIdentity(GpuResource Resource, GpuResourceRegistration? Registration,
    nint Name, ulong StorageRevision, RHITextureSubresource Subresource, bool Layered, GpuImageMetadata Image)
{
    #region Internal API
    /// <summary>Checks the captured original allocation and projection without acquiring missing facts or accepting name reuse.</summary>
    internal bool IsCurrent
    {
        get
        {
            try
            {
                var selected = Resource is GpuResource<GpuRenderbufferMetadata> renderbuffer
                    ? RHIAttachment.FromRenderbuffer(new(renderbuffer))
                    : RHIAttachment.FromTexture(new((GpuResource<GpuTextureMetadata>)Resource), Subresource, Layered);
                return this == Resolve(selected);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return false; }
        }
    }
    /// <summary>Projects already published facts without native reads, image-wrapper allocation or target realization.</summary>
    internal static OpenGLAttachmentIdentity Resolve(in RHIAttachment attachment)
    {
        GpuResource resource;
        GpuImageMetadata image;
        if (!attachment.Texture.IsEmpty)
        {
            var data = attachment.Texture.RequireMetadata();
            resource = attachment.Texture.Resource!;
            bool cube = data.Target == TextureTarget.TextureCubeMap;
            bool array = data.Target is TextureTarget.Texture1DArray or TextureTarget.Texture2DArray
                or TextureTarget.Texture3D or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray;
            if (attachment.Layered && !cube && !array || !attachment.Layered && !cube && !array && attachment.Subresource.Layer != 0)
                throw new ArgumentException("The supplied image selection is incompatible with its published target.");
            var face = cube && !attachment.Layered ? TextureTarget.TextureCubeMapPositiveX + attachment.Subresource.Layer : 0;
            if (cube && !attachment.Layered && (uint)attachment.Subresource.Layer >= 6)
                throw new ArgumentOutOfRangeException(nameof(attachment));
            image = GpuResourceManager.RequireTextureAttachmentMetadata(data,
                new(attachment.Subresource.MipLevel, array ? attachment.Subresource.Layer : 0, face, attachment.Layered));
        }
        else
        {
            if (!attachment.Renderbuffer.TryGetMetadata(out var data))
                throw new InvalidOperationException("Renderbuffer metadata is unavailable.");
            resource = attachment.Renderbuffer.RequireResource();
            image = new(data.Width, data.Height, 1, data.Format, data.Samples);
        }
        var registration = resource.ResourceManager?.GetResourceRegistration(resource);
        return new(resource, registration, resource.ResourceId, registration?.StorageRevision ?? 0,
            attachment.Subresource, attachment.Layered, image);
    }

    /// <summary>Builds the existing manager-owned image projection only for a target cache miss.</summary>
    internal GpuFramebufferAttachment CreateImage()
    {
        if (Resource is GpuResource<GpuRenderbufferMetadata> renderbuffer)
            return GpuFramebufferAttachment.FromRenderbufferReference(new(renderbuffer));
        var texture = new GpuTextureRef((GpuResource<GpuTextureMetadata>)Resource);
        bool cube = texture.Target == TextureTarget.TextureCubeMap;
        bool array = texture.Target is TextureTarget.Texture1DArray or TextureTarget.Texture2DArray
            or TextureTarget.Texture3D or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray;
        return GpuFramebufferAttachment.FromTexture(texture, Subresource.MipLevel,
            !Layered && array ? Subresource.Layer : null,
            !Layered && cube ? TextureTarget.TextureCubeMapPositiveX + Subresource.Layer : null);
    }
    #endregion
}
