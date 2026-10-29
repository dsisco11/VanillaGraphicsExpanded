using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System;
using System.Runtime.ExceptionServices;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes explicit framebuffer image transfers and format conversion without native selector escapes.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void BlitImages(in RHIImageBlit blit)
    {
        RequireContext();
        if (HasRenderPass) throw new InvalidOperationException("End the render pass before transferring framebuffer images.");
        var source = OpenGLAttachmentIdentity.Resolve(blit.Source);
        var destination = OpenGLAttachmentIdentity.Resolve(blit.Destination);
        ValidateImageBlit(blit, source, destination);
        if (executionResources.Count != 0) throw new InvalidOperationException("Resource execution cannot recurse.");
        try
        {
            RetainExecutionResource(source.Resource);
            RetainExecutionResource(destination.Resource);
            var read = ResolvePassTarget(TransferTarget(blit.Source, blit.Aspects));
            var draw = ResolvePassTarget(TransferTarget(blit.Destination, blit.Aspects));
            RetainExecutionResource(read); RetainExecutionResource(draw);
            var state = cache.PreserveImageTransferState();
            Exception? failure = null;
            try
            {
                cache.SetCapability(OpenTK.Graphics.OpenGL.EnableCap.ScissorTest, false);
                cache.SetFramebufferSrgbEnabled(false);
                var mask = (blit.Aspects.HasFlag(ImageAspects.Color) ? ClearBufferMask.ColorBufferBit : 0)
                    | (blit.Aspects.HasFlag(ImageAspects.Depth) ? ClearBufferMask.DepthBufferBit : 0)
                    | (blit.Aspects.HasFlag(ImageAspects.Stencil) ? ClearBufferMask.StencilBufferBit : 0);
                var from = blit.SourceArea; var to = blit.DestinationArea;
                GL.BlitNamedFramebuffer(read.FboId, draw.FboId, from.X, from.Y, checked(from.X + from.Width), checked(from.Y + from.Height),
                    to.X, to.Y, checked(to.X + to.Width), checked(to.Y + to.Height), mask,
                    blit.Filter == SamplerFilter.Linear ? BlitFramebufferFilter.Linear : BlitFramebufferFilter.Nearest);
                RequireContext();
                if (!source.IsCurrent || !destination.IsCurrent)
                    throw new InvalidOperationException("The original image storage became unavailable during transfer.");
            }
            catch (Exception error) { failure = error; }
            try { state.Dispose(); }
            catch (Exception restoration)
            {
                if (failure is not null) throw new AggregateException(failure, restoration);
                throw;
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { ReleaseExecutionResources(); }
    }
    #endregion

    #region Private
    /// <summary>Projects one transfer selection to the same context-owned target service used by rendering.</summary>
    private static RHIRenderPassDescription TransferTarget(RHIAttachment image, ImageAspects aspects)
        => aspects == ImageAspects.Color ? new([new(Image: image)]) : new([], depthStencil: image);

    /// <summary>Rejects unsupported conversion, resolve, filtering and overlap before target realization or native state effects.</summary>
    private static void ValidateImageBlit(in RHIImageBlit blit, in OpenGLAttachmentIdentity source, in OpenGLAttachmentIdentity destination)
    {
        if (blit.Aspects == 0 || (blit.Aspects & ~(ImageAspects.Color | ImageAspects.Depth | ImageAspects.Stencil)) != 0
            || blit.Aspects.HasFlag(ImageAspects.Color) && blit.Aspects != ImageAspects.Color
            || blit.Filter is not (SamplerFilter.Nearest or SamplerFilter.Linear)
            || blit.Source.Layered || blit.Destination.Layered)
            throw new ArgumentException("An image blit requires single-image selections, represented aspects and nearest/linear filtering.");
        ValidateBlitArea(blit.SourceArea, source.Image);
        ValidateBlitArea(blit.DestinationArea, destination.Image);
        var fromFormat = source.Image.Format;
        var toFormat = destination.Image.Format;
        bool color = blit.Aspects == ImageAspects.Color;
        if (color)
        {
            if (!TargetFormatPolicy.IsColor(fromFormat) || !TargetFormatPolicy.IsColor(toFormat))
                throw new ArgumentException("A color blit requires color storage at both ends.");
            bool fromInteger = TargetFormatPolicy.IsInteger(fromFormat), toInteger = TargetFormatPolicy.IsInteger(toFormat);
            if (fromInteger != toInteger || fromInteger && SignedIntegerFormat(fromFormat) != SignedIntegerFormat(toFormat)
                || fromInteger && blit.Filter != SamplerFilter.Nearest)
                throw new ArgumentException("Integer color transfers require compatible scalar classes and nearest filtering.");
        }
        else if (source.Image.Format != destination.Image.Format || blit.Filter != SamplerFilter.Nearest
            || blit.Aspects.HasFlag(ImageAspects.Depth) && !TextureFormatHelper.IsDepthFormat(source.Image.Format)
            || blit.Aspects.HasFlag(ImageAspects.Stencil) && !StencilFormat(source.Image.Format))
            throw new ArgumentException("Depth/stencil blits require matching representations, present aspects and nearest filtering.");
        int fromSamples = Math.Max(1, source.Image.Samples), toSamples = Math.Max(1, destination.Image.Samples);
        if (toSamples > fromSamples || toSamples > 1 && toSamples != fromSamples
            || (fromSamples > 1 || toSamples > 1) && (blit.SourceArea.Width != blit.DestinationArea.Width
                || blit.SourceArea.Height != blit.DestinationArea.Height || blit.Filter != SamplerFilter.Nearest)
            || fromSamples > 1 && source.Image.Format != destination.Image.Format)
            throw new ArgumentException("Multisample blits require equal rectangles and matching formats and cannot increase the sample count.");
        if (OverlappingBlitImages(source, destination)
            && blit.SourceArea.X < (long)blit.DestinationArea.X + blit.DestinationArea.Width
            && blit.DestinationArea.X < (long)blit.SourceArea.X + blit.SourceArea.Width
            && blit.SourceArea.Y < (long)blit.DestinationArea.Y + blit.DestinationArea.Height
            && blit.DestinationArea.Y < (long)blit.SourceArea.Y + blit.SourceArea.Height)
            throw new ArgumentException("Source and destination blit regions overlap the same storage image.");
    }
    /// <summary>Rejects rectangles outside the selected published image before checked endpoint arithmetic or native dispatch.</summary>
    private static void ValidateBlitArea(in RenderArea area, in GpuImageMetadata image)
    {
        if (area.X < 0 || area.Y < 0 || area.Width <= 0 || area.Height <= 0
            || (long)area.X + area.Width > image.Width || (long)area.Y + area.Height > image.Height)
            throw new ArgumentOutOfRangeException(nameof(area));
    }
    /// <summary>Compares original backing images, accounting for view-relative mip and layer offsets.</summary>
    private static bool OverlappingBlitImages(in OpenGLAttachmentIdentity source, in OpenGLAttachmentIdentity destination)
    {
        if (source.Resource.Kind != destination.Resource.Kind) return false;
        if (source.Resource.Kind == GpuResourceKind.Renderbuffer) return source.Name == destination.Name;
        var from = RHIResourceManager.TextureStorageSelection(source.Resource);
        var to = RHIResourceManager.TextureStorageSelection(destination.Resource);
        nint fromStorage = RHIResourceManager.TextureStorageName(source.Resource);
        nint toStorage = RHIResourceManager.TextureStorageName(destination.Resource);
        return fromStorage == toStorage && source.Subresource.MipLevel + (from?.BaseLevel ?? 0)
                == destination.Subresource.MipLevel + (to?.BaseLevel ?? 0)
            && source.Subresource.Layer + (from?.BaseLayer ?? 0) == destination.Subresource.Layer + (to?.BaseLayer ?? 0);
    }
    /// <summary>Recognizes packed depth/stencil storage in addition to stencil-only images.</summary>
    private static bool StencilFormat(TextureFormat format)
        => TextureFormatHelper.IsStencilFormat(format) || format is TextureFormat.Depth24Stencil8
            or TextureFormat.Depth32fStencil8;
    /// <summary>Classifies signed integer attachments without native queries or numeric token inference.</summary>
    private static bool SignedIntegerFormat(TextureFormat format) => format is TextureFormat.R8i or TextureFormat.R16i or TextureFormat.R32i
        or TextureFormat.Rg8i or TextureFormat.Rg16i or TextureFormat.Rg32i or TextureFormat.Rgb8i or TextureFormat.Rgb16i
        or TextureFormat.Rgb32i or TextureFormat.Rgba8i or TextureFormat.Rgba16i or TextureFormat.Rgba32i;
    #endregion
}
