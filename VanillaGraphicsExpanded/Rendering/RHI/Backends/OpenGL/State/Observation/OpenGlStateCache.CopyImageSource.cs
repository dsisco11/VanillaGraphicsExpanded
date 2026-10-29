using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Classifies copy definitions using the already executed read selector and maintained source images.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Private
    /// <summary>Distinguishes established source compatibility, proven rejection and unknown surface/backing facts.</summary>
    private bool? ClassifyCopyImageSource(TextureFormat format)
    {
        if (!TryGetCachedCurrentFramebuffer(FramebufferTarget.ReadFramebuffer, out int id) || id == 0) return null;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        if (manager.FindObservedResource(GpuResourceKind.Framebuffer, id) is not GpuResourceMetadataEntry<GpuFramebufferMetadata> { IsUsable: true } framebuffer) return null;
        // Every attached image contributes to completeness, including attachments
        // other than the selected read image. Do not certify a copy from an
        // incomplete framebuffer or one whose backing facts are unavailable.
        int? attachmentSamples = null;
        bool? layered = null;
        TextureDimension? layeredColorTarget = null;
        GpuFramebufferImageMetadata? depthAttachment = null, stencilAttachment = null;
        bool uncertain = false;
        foreach (var pair in framebuffer.Metadata.Attachments)
        {
            var attachment = pair.Value;
            var attachedImage = manager.ProjectFramebufferDescriptor(attachment);
            if (!attachedImage.HasValue) return null;
            if (attachedImage.Value.Width <= 0 || attachedImage.Value.Height <= 0 || attachedImage.Value.Depth <= 0) return false;
            if (attachedImage.Value.Width > GpuSupport.Graphics.MaxFramebufferWidth
                || attachedImage.Value.Height > GpuSupport.Graphics.MaxFramebufferHeight
                || attachment.Layered && attachedImage.Value.Depth > GpuSupport.Graphics.MaxFramebufferLayers) return false;
            if (attachmentSamples.HasValue && attachmentSamples.Value != attachedImage.Value.Samples) return false;
            attachmentSamples = attachedImage.Value.Samples;
            if (layered.HasValue && layered.Value != attachment.Layered) return false;
            layered = attachment.Layered;
            var attachedFormat = attachedImage.Value.Format;
            bool depth = TextureFormatHelper.IsDepthFormat(attachedFormat) || attachedFormat == TextureFormat.DepthStencil;
            bool stencil = TextureFormatHelper.IsStencilFormat(attachedFormat)
                || attachedFormat is TextureFormat.DepthStencil or TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8;
            if (pair.Key == FramebufferSlot.Depth && !depth || pair.Key == FramebufferSlot.Stencil && !stencil
                || pair.Key != FramebufferSlot.Depth && pair.Key != FramebufferSlot.Stencil && (depth || stencil)) return false;
            if (pair.Key == FramebufferSlot.Depth)
            {
                depthAttachment = attachment;
                if (attachedFormat is not (TextureFormat.DepthComponent16 or TextureFormat.DepthComponent24
                    or TextureFormat.DepthComponent32f or TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8)) uncertain = true;
            }
            else if (pair.Key == FramebufferSlot.Stencil)
            {
                stencilAttachment = attachment;
                if (attachedFormat is not (TextureFormat.StencilIndex8 or TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8)) uncertain = true;
            }
            else
            {
                // The represented sized color formats are color-renderable in
                // core GL, except RGB9_E5 and the specific compressed formats.
                if (attachedFormat == TextureFormat.Rgb9E5 || CompressedImageBlockBytes(attachedFormat) != 0) return false;
                if (attachedFormat == TextureFormat.Undefined || !GpuTextureFormatReader.IsExplicitFormat(attachedFormat)) uncertain = true;
            }
            if (attachment.OriginalBacking is GpuResourceMetadataEntry<GpuTextureMetadata> backing && !backing.Metadata.ImmutableStorage)
            {
                if (attachment.MipLevel < backing.Metadata.BaseLevel || backing.Metadata.BaseLevel > backing.Metadata.MaxLevel) return false;
                if (attachment.MipLevel != backing.Metadata.BaseLevel || backing.Metadata.Target == TextureDimension.Cube) uncertain = true;
            }
            if (attachment.Layered && pair.Key != FramebufferSlot.Depth && pair.Key != FramebufferSlot.Stencil)
            {
                if (attachment.OriginalBacking is not GpuResourceMetadataEntry<GpuTextureMetadata> texture) return null;
                if (layeredColorTarget.HasValue && layeredColorTarget != texture.Metadata.Target) return false;
                layeredColorTarget = texture.Metadata.Target;
            }
        }
        // CopyTexImage cannot read multisample framebuffer storage, irrespective
        // of the fixed-sample-location completeness rule.
        if (attachmentSamples > 0) return false;
        if (depthAttachment.HasValue && stencilAttachment.HasValue
            && (depthAttachment.Value with { Mode = GpuFramebufferAttachmentMode.Unknown })
                != (stencilAttachment.Value with { Mode = GpuFramebufferAttachmentMode.Unknown })) uncertain = true;
        if (uncertain) return null;
        FramebufferSlot slot;
        if (TextureFormatHelper.IsDepthFormat(format)) slot = FramebufferSlot.Depth;
        else
        {
            if (!TryGetFramebufferReadRouting(id, out var route)) return null;
            if (route == ReadBufferMode.None) return false;
            slot = FramebufferSlot.Color0 + ((int)route - (int)ReadBufferMode.ColorAttachment0);
        }
        if (!framebuffer.Metadata.Attachments.TryGetValue(slot, out var descriptor)) return false;
        var image = manager.ProjectFramebufferDescriptor(descriptor);
        if (!image.HasValue) return null;
        if (image.Value.Samples > 0) return false;
        bool fromInteger = TextureFormatHelper.GetPixelFormat(image.Value.Format) is PixelChannelLayout.RedInteger
            or PixelChannelLayout.RgInteger or PixelChannelLayout.RgbInteger or PixelChannelLayout.RgbaInteger;
        bool toInteger = TextureFormatHelper.GetPixelFormat(format) is PixelChannelLayout.RedInteger
            or PixelChannelLayout.RgInteger or PixelChannelLayout.RgbInteger or PixelChannelLayout.RgbaInteger;
        if (fromInteger != toInteger) return false;
        if (fromInteger)
        {
            bool fromSigned = TextureFormatHelper.GetPixelType(image.Value.Format) is PixelComponentType.Byte or PixelComponentType.Short or PixelComponentType.Int;
            bool toSigned = TextureFormatHelper.GetPixelType(format) is PixelComponentType.Byte or PixelComponentType.Short or PixelComponentType.Int;
            if (fromSigned != toSigned) return false;
        }
        return true;
    }
    #endregion
}
