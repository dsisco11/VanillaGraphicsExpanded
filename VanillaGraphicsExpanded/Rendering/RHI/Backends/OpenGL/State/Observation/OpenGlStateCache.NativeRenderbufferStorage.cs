using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains renderbuffer allocation facts from completed native requests.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Publishes supplied extent/format and only the approved sample-count result.</summary>
    internal static void ObserveRenderbufferStorage(int selector, bool named, int nativeFormat,
        int width, int height, int samples, bool extNamed = false, bool coverage = false, int coverageSamples = 0)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        if (!named && selector != (int)RenderbufferTarget.Renderbuffer) return;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        int id = named ? selector : cache.GetCurrentRenderbuffer();
        if (extNamed) manager.RealizeObservedImage(GpuResourceKind.Renderbuffer, id, implicitCreation: true);
        var entry = manager.FindObservedResource(GpuResourceKind.Renderbuffer, id) as GpuResourceMetadataEntry<GpuRenderbufferMetadata>;
        if (entry?.IsRealized != true || width < 0 || height < 0 || samples < 0
            || coverage && (coverageSamples < 0 || samples > coverageSamples)) return;
        TextureFormat format;
        try { format = OpenGLImageValues.Format((PixelInternalFormat)nativeFormat); }
        catch (System.ArgumentOutOfRangeException) { return; }
        if (!GpuTextureFormatReader.IsExplicitFormat(format) || CompressedImageBlockBytes(format) != 0) return;
        if (GpuSupport.TryGetCachedFormatCapabilities(TextureDimension.Two, format, out var cachedFormat, GpuResourceKind.Renderbuffer)
            && (!cachedFormat.Supported || cachedFormat.FramebufferRenderableSupport == 0)) return;
        if (GpuSupport.HasCachedCapabilitiesForCurrentContext
            && (width > GpuSupport.Graphics.MaxRenderbufferSize || height > GpuSupport.Graphics.MaxRenderbufferSize
                || samples > GpuSupport.Graphics.MaxSamples || coverage && coverageSamples > GpuSupport.Graphics.MaxSamples)) return;
        if (!entry.IsUsable || !GpuSupport.HasCachedCapabilitiesForCurrentContext)
        {
            cache.InterruptNativeResourceOperation(GpuResourceKind.Renderbuffer, id);
            if (!InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) entry.BeginRefresh(GpuMetadataChange.Storage);
            return;
        }
        int requestedSamples = coverage ? coverageSamples : samples;
        int actualSamples = requestedSamples;
        bool resolved = requestedSamples == 0;
        if (!coverage && samples > 0 && GpuSupport.TryGetCachedFormatCapabilities(TextureDimension.Two, format,
            out var support, GpuResourceKind.Renderbuffer) && !support.SampleCounts.IsDefaultOrEmpty)
        {
            actualSamples = int.MaxValue;
            foreach (int count in support.SampleCounts)
                if (count >= samples && count < actualSamples) actualSamples = count;
            if (actualSamples == int.MaxValue) return;
            resolved = true;
        }
        if (!resolved) GL.GetNamedRenderbufferParameter(id, RenderbufferParameterName.RenderbufferSamples, out actualSamples);
        // An unchanged old allocation cannot prove that a replacement succeeded.
        if (actualSamples < requestedSamples) return;
        if (!resolved && entry.Metadata.Samples == actualSamples
            && (entry.Metadata.Width != width || entry.Metadata.Height != height || entry.Metadata.Format != format))
        {
            cache.InterruptNativeResourceOperation(GpuResourceKind.Renderbuffer, id);
            if (!InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) entry.BeginRefresh(GpuMetadataChange.Storage);
            return;
        }
        cache.InterruptNativeResourceOperation(GpuResourceKind.Renderbuffer, id);
        if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) return;
        manager.PublishMetadata(entry, new(width, height, format, actualSamples), GpuMetadataChange.Storage);
    }
    #endregion
}
