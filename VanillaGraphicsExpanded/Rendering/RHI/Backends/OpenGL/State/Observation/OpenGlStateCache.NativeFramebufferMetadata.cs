using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains framebuffer attachments and defaults from individual native effects.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Private
    /// <summary>Identifies targets accepted by two-dimensional attachment entrypoints.</summary>
    private static bool IsFramebufferTexture2DTarget(TextureTarget target) => target is TextureTarget.Texture2D
        or TextureTarget.TextureRectangle or TextureTarget.Texture1DArray or TextureTarget.Texture2DMultisample
        or TextureTarget.TextureCubeMapPositiveX or TextureTarget.TextureCubeMapNegativeX
        or TextureTarget.TextureCubeMapPositiveY or TextureTarget.TextureCubeMapNegativeY
        or TextureTarget.TextureCubeMapPositiveZ or TextureTarget.TextureCubeMapNegativeZ;

    /// <summary>Publishes a selected descriptor without scanning native slots or backing images.</summary>
    private static void ObserveFramebufferMutation(int selector, bool named, int slot = 0, int texture = 0,
        int level = 0, int layer = -1, int family = 3, FramebufferDefaultParameter? parameter = null,
        int value = 0, int imageTarget = 0, int renderbuffer = 0, bool extNamed = false)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        if (!named && (FramebufferTarget)selector is not (FramebufferTarget.Framebuffer or FramebufferTarget.DrawFramebuffer or FramebufferTarget.ReadFramebuffer)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        int id = named ? selector : cache.GetCurrentFramebuffer((FramebufferTarget)selector);
        if (extNamed) cache.RealizeFramebuffer(id, implicitCreation: true);
        var entry = manager.FindObservedResource(GpuResourceKind.Framebuffer, id) as GpuResourceMetadataEntry<GpuFramebufferMetadata>;
        if (entry?.IsRealized != true) return;
        // Scalar rejection is independent of snapshot availability. A managed operation
        // withdraws its candidate while executing; invalid external calls must not interrupt it.
        if (parameter.HasValue)
        {
            if (parameter.Value is not (FramebufferDefaultParameter.FramebufferDefaultWidth
                or FramebufferDefaultParameter.FramebufferDefaultHeight or FramebufferDefaultParameter.FramebufferDefaultLayers
                or FramebufferDefaultParameter.FramebufferDefaultSamples or FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations)) return;
            if (parameter != FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations && value < 0) return;
        }
        else
        {
            bool attachmentToken = slot is (int)FramebufferAttachment.DepthAttachment or (int)FramebufferAttachment.StencilAttachment
                or (int)FramebufferAttachment.DepthStencilAttachment
                || slot >= (int)FramebufferAttachment.ColorAttachment0 && slot <= (int)FramebufferAttachment.ColorAttachment31;
            if (!attachmentToken || level < 0 || family == 1 && layer < 0
                || family == 2 && imageTarget != (int)RenderbufferTarget.Renderbuffer) return;
        }
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext) { WithdrawFramebufferDefinition(cache, entry); return; }
        var limits = GpuSupport.Graphics;
        // Captured limits can prove rejection even while the previous descriptor is withdrawn.
        if (parameter.HasValue)
        {
            int max = parameter.Value switch
            {
                FramebufferDefaultParameter.FramebufferDefaultWidth => limits.MaxFramebufferWidth,
                FramebufferDefaultParameter.FramebufferDefaultHeight => limits.MaxFramebufferHeight,
                FramebufferDefaultParameter.FramebufferDefaultLayers => limits.MaxFramebufferLayers,
                FramebufferDefaultParameter.FramebufferDefaultSamples => limits.MaxFramebufferSamples,
                _ => int.MaxValue
            };
            if (!limits.SupportsFramebufferNoAttachments || value > max) return;
        }
        else if (slot >= (int)FramebufferAttachment.ColorAttachment0 && slot <= (int)FramebufferAttachment.ColorAttachment31
            && (long)slot - (int)FramebufferAttachment.ColorAttachment0 >= limits.MaxColorAttachments) return;
        var candidate = entry.Metadata;
        if (parameter.HasValue)
        {
            candidate = parameter.Value switch
            {
                FramebufferDefaultParameter.FramebufferDefaultWidth => candidate with { DefaultWidth = value },
                FramebufferDefaultParameter.FramebufferDefaultHeight => candidate with { DefaultHeight = value },
                FramebufferDefaultParameter.FramebufferDefaultLayers => candidate with { DefaultLayers = value },
                FramebufferDefaultParameter.FramebufferDefaultSamples => candidate with { DefaultSamples = value },
                _ => candidate with { DefaultFixedSampleLocations = value != 0 }
            };
        }
        else
        {
            bool rb = family == 2;
            int backingId = rb ? renderbuffer : texture;
            var source = manager.FindObservedResource(rb ? GpuResourceKind.Renderbuffer : GpuResourceKind.Texture, backingId);
            if (backingId != 0 && source?.IsRealized != true) return;
            CubeFace face = CubeFace.None;
            bool layered = false;
            if (!rb && backingId != 0 && source is GpuResourceMetadataEntry<GpuTextureMetadata> { HasCommittedMetadata: true } image)
            {
                var target = image.Metadata.Target;
                if (target is TextureDimension.Buffer or TextureDimension.RenderbufferTexture) return;
                int extent = target == TextureDimension.Three ? limits.Max3DTextureSize
                    : target is TextureDimension.Cube or TextureDimension.CubeArray ? limits.MaxCubeMapTextureSize
                    : target == TextureDimension.Rectangle ? limits.MaxRectangleTextureSize : limits.MaxTextureSize;
                int maxLevel = 0;
                while ((extent >>= 1) > 0) maxLevel++;
                if (level > maxLevel || target is TextureDimension.Rectangle or TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray && level != 0) return;
                if (family == 1 && layer >= (target == TextureDimension.Cube ? 6
                    : target == TextureDimension.Three ? System.Math.Max(1, limits.Max3DTextureSize >> level) : limits.MaxArrayTextureLayers)) return;
                if (family == 1 && target is not (TextureDimension.Three or TextureDimension.OneArray or TextureDimension.TwoArray or TextureDimension.Cube or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray)) return;
                if (imageTarget != 0)
                {
                    bool cube = imageTarget >= (int)TextureTarget.TextureCubeMapPositiveX && imageTarget <= (int)TextureTarget.TextureCubeMapNegativeZ;
                    if (cube) { if (target != TextureDimension.Cube) return; face = OpenGLImageValues.Face((TextureTarget)imageTarget); }
                    else if (!TryImageTarget(imageTarget, out var supplied) || supplied != target) return;
                }
                layered = family == 0 && target is TextureDimension.Three or TextureDimension.OneArray or TextureDimension.TwoArray or TextureDimension.Cube or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray;
                if (family == 1 && target == TextureDimension.Cube)
                {
                    if (layer >= 6) return;
                    face = CubeFace.PositiveX + layer;
                }
            }
            // A published target is fixed for the object's lifetime even while storage facts
            // are withdrawn. Use it above only to prove rejection; unresolved storage still
            // cannot become a new attachment descriptor or interrupt a rejected request.
            if (backingId != 0 && source?.IsUsable != true) { WithdrawFramebufferDefinition(cache, entry); return; }
            var descriptor = new GpuFramebufferImageMetadata(rb ? GpuResourceKind.Renderbuffer : GpuResourceKind.Texture,
                backingId, rb ? 0 : level, face == CubeFace.None ? Math.Max(0, layer) : 0, face, layered, Mode: GpuFramebufferAttachmentMode.Ordinary)
                { OriginalBacking = source };
            // Only after rejection classification may an unresolved descriptor interrupt its owner.
            if (!entry.IsUsable) { WithdrawFramebufferDefinition(cache, entry); return; }
            var attachments = candidate.Attachments;
            FramebufferSlot selected = OpenGLResourceValues.FromNative((FramebufferAttachment)slot);
            if (selected == FramebufferSlot.DepthStencil)
                attachments = backingId == 0 ? attachments.Remove(FramebufferSlot.Depth).Remove(FramebufferSlot.Stencil)
                    : attachments.SetItem(FramebufferSlot.Depth, descriptor).SetItem(FramebufferSlot.Stencil, descriptor);
            else attachments = backingId == 0 ? attachments.Remove(selected) : attachments.SetItem(selected, descriptor);
            candidate = candidate with { Attachments = attachments };
        }
        if (!entry.IsUsable) { WithdrawFramebufferDefinition(cache, entry); return; }
        cache.InterruptNativeResourceOperation(GpuResourceKind.Framebuffer, id);
        if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Metadata)) return;
        manager.PublishMetadata(entry, candidate, GpuMetadataChange.Metadata);

    }

    /// <summary>Contains unavailable framebuffer effects without changing unrelated executed knowledge.</summary>
    private static void WithdrawFramebufferDefinition(OpenGlStateCache cache, GpuResourceMetadataEntry<GpuFramebufferMetadata> entry)
    {
        cache.InterruptNativeResourceOperation(GpuResourceKind.Framebuffer, (int)entry.Id);
        if (!InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Metadata)) entry.Withdraw();

    }
    #endregion
}
