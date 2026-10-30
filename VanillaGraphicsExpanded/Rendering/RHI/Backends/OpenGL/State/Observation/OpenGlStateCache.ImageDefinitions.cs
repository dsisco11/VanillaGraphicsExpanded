using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Interprets supplied native image definitions beneath the portable resource contracts.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Applies an established definition, preserving proven rejection and containing unknown outcomes.</summary>
    internal static void ObserveTextureDefinition(int selector, int nativeTarget, int nativeUnit, bool named,
        bool explicitUnit, int level, int nativeFormat, int width, int height, int depth, int levels,
        bool immutable, int border, int samples, int axes, bool extNamed = false, int memory = 0,
        int pixelFormat = 0, int pixelType = 0, int? compressedSize = null, bool copy = false,
        bool externalMemory = false, ulong memoryOffset = 0, nint? pixels = null)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var entry = ResolveObservedTexture(selector, nativeTarget, nativeUnit, named, explicitUnit, extNamed);
        if (entry is null || !entry.IsRealized) return;
        var data = entry.Metadata;
        bool multi = data.Target is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray;
        // These restrictions establish rejection independently of optional capability discovery.
        if (data.ImmutableStorage || data.Target is TextureDimension.Buffer or TextureDimension.RenderbufferTexture
            || level < 0 || levels <= 0 || border != 0 || width < 0 || height < 0 || depth < 0
            || immutable && (width == 0 || height == 0 || depth == 0)
            || multi && (level != 0 || samples <= 0) || !multi && samples != 0
            || data.Target == TextureDimension.Rectangle && (level != 0 || levels != 1)
            || data.Target is TextureDimension.Cube or TextureDimension.CubeArray && width != height
            || data.Target == TextureDimension.CubeArray && depth % 6 != 0) return;
        int requiredAxes = data.Target == TextureDimension.One ? 1
            : data.Target is TextureDimension.Three or TextureDimension.TwoArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray ? 3 : 2;
        if (axes != requiredAxes) return;
        bool cubeFace = nativeTarget >= (int)TextureTarget.TextureCubeMapPositiveX && nativeTarget <= (int)TextureTarget.TextureCubeMapNegativeZ;
        if (data.Target == TextureDimension.Cube && (immutable ? cubeFace : !cubeFace)) return;
        TextureFormat format;
        try { format = OpenGLImageValues.Format((PixelInternalFormat)nativeFormat); }
        catch (ArgumentOutOfRangeException)
        {
            // A known native image format can be valid even if the portable schema
            // cannot represent it; keep the affected result unavailable rather
            // than preserving stale storage facts as a proven rejection.
            if (Enum.IsDefined((PixelInternalFormat)nativeFormat) || Enum.IsDefined((InternalFormat)nativeFormat))
                WithdrawImageDefinition(cache, entry);
            return;
        }
        if (immutable && !GpuTextureFormatReader.IsExplicitFormat(format)) return;
        if (compressedSize is < 0 || externalMemory && memory == 0) return;
        if (pixelFormat != 0)
        {
            if (!Enum.IsDefined((PixelFormat)pixelFormat) || !Enum.IsDefined((PixelType)pixelType)) return;
            bool integerPixels = (PixelFormat)pixelFormat is PixelFormat.RedInteger or PixelFormat.RgInteger
                or PixelFormat.RgbInteger or PixelFormat.RgbaInteger or PixelFormat.BgrInteger or PixelFormat.BgraInteger;
            bool integerStorage = TextureFormatHelper.GetPixelFormat(format) is PixelChannelLayout.RedInteger
                or PixelChannelLayout.RgInteger or PixelChannelLayout.RgbInteger or PixelChannelLayout.RgbaInteger;
            if (integerPixels != integerStorage) return;
            bool depthStorage = TextureFormatHelper.IsDepthFormat(format) || format == TextureFormat.DepthStencil;
            if (depthStorage != ((PixelFormat)pixelFormat is PixelFormat.DepthComponent or PixelFormat.DepthStencil)) return;
            if (((PixelFormat)pixelFormat == PixelFormat.StencilIndex) != TextureFormatHelper.IsStencilFormat(format)) return;
            bool packedDepthStencil = (PixelType)pixelType is PixelType.UnsignedInt248 or PixelType.Float32UnsignedInt248Rev;
            if (((PixelFormat)pixelFormat == PixelFormat.DepthStencil) != packedDepthStencil) return;
            try { _ = OpenGLPixelValues.Encoding((PixelFormat)pixelFormat, (PixelType)pixelType).BytesPerPixel; }
            catch (ArgumentException) { WithdrawImageDefinition(cache, entry); return; }
        }
        if (compressedSize is { } bytes)
        {
            int block = CompressedImageBlockBytes(format);
            if (block == 0) return;
            long expected = ((long)width + 3) / 4 * (((long)height + 3) / 4) * depth * block;
            if (expected != bytes) return;
        }
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext)
        {
            WithdrawImageDefinition(cache, entry);
            return;
        }
        var caps = GpuSupport.Graphics;
        int max = data.Target == TextureDimension.Three ? caps.Max3DTextureSize
            : data.Target == TextureDimension.Rectangle ? caps.MaxRectangleTextureSize
            : data.Target is TextureDimension.Cube or TextureDimension.CubeArray ? caps.MaxCubeMapTextureSize : caps.MaxTextureSize;
        int mipLimit = 0;
        for (int n = max; n > 1; n >>= 1) mipLimit++;
        if (level > mipLimit || width > Math.Max(1, max >> level)
            || data.Target != TextureDimension.OneArray && height > Math.Max(1, max >> level)
            || data.Target == TextureDimension.Three && depth > Math.Max(1, max >> level)
            || data.Target == TextureDimension.OneArray && height > caps.MaxArrayTextureLayers
            || data.Target is TextureDimension.TwoArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray && depth > caps.MaxArrayTextureLayers) return;
        if (immutable)
        {
            int extent = Math.Max(width, data.Target == TextureDimension.OneArray ? 1 : height);
            if (data.Target == TextureDimension.Three) extent = Math.Max(extent, depth);
            int count = 1;
            while ((extent >>= 1) > 0) count++;
            if (levels > count || multi && levels != 1) return;
        }
        if (pixels.HasValue)
        {
            bool? transfer = cache.ClassifyImageUnpack(pixels.Value, width, height, depth, axes,
                pixelFormat, pixelType, compressedSize);
            if (transfer == false) return;
            if (transfer is null) { WithdrawImageDefinition(cache, entry); return; }
        }
        bool? copyAccepted = copy ? cache.ClassifyCopyImageSource(format) : true;
        if (copyAccepted == false) return;
        if (!entry.IsUsable || copyAccepted is null)
        {
            WithdrawImageDefinition(cache, entry);
            return;
        }
        if (externalMemory)
        {
            // Imported payload compatibility is opaque to the observer. The entry was
            // known mutable before dispatch, so this flag establishes the storage transition
            // without reading image descriptors or consuming the application's error state.
            GL.GetTextureParameter((int)entry.Id, GetTextureParameter.TextureImmutableFormat, out int established);
            if (established == 0) return;
        }
        int resolvedSamples = samples;
        if (multi && GpuSupport.TryGetCachedFormatCapabilities(data.Target, format, out var support)
            && !support.SampleCounts.IsDefaultOrEmpty)
        {
            resolvedSamples = int.MaxValue;
            foreach (int count in support.SampleCounts)
                if (count >= samples && count < resolvedSamples) resolvedSamples = count;
            if (resolvedSamples == int.MaxValue) return;
        }
        else if (multi)
        {
            GL.GetTextureLevelParameter((int)entry.Id, level, GetTextureParameter.TextureSamples, out resolvedSamples);
            // A successful allocation must produce at least the requested count.
            // An unchanged old count cannot establish whether mutable reallocation succeeded.
            if (resolvedSamples < samples) return;
            foreach (var previous in data.Levels)
                if (previous.Level == level && previous.Allocated && previous.Samples == resolvedSamples)
                { WithdrawImageDefinition(cache, entry); return; }
        }
        CubeFace face = cubeFace ? OpenGLImageValues.Face((TextureTarget)nativeTarget) : CubeFace.None;
        if (!GpuTextureFormatReader.IsExplicitFormat(format) && width > 0 && height > 0 && depth > 0)
        {
            int resolved;
            if (cubeFace)
            {
                using var scope = cache.BindTextureScope(TextureTarget.TextureCubeMap, 0, (int)entry.Id);
                GL.GetTexLevelParameter((TextureTarget)nativeTarget, level, GetTextureParameter.TextureInternalFormat, out resolved);
            }
            else GL.GetTextureLevelParameter((int)entry.Id, level, GetTextureParameter.TextureInternalFormat, out resolved);
            if (resolved <= 0) { WithdrawImageDefinition(cache, entry); return; }
            format = OpenGLImageValues.Format((PixelInternalFormat)resolved);
        }
        if (resolvedSamples < 0) { WithdrawImageDefinition(cache, entry); return; }
        cache.InterruptNativeResourceOperation(GpuResourceKind.Texture, (int)entry.Id);
        if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) return;
        entry.Manager.PublishTextureDefinition(entry, new(level, face, width, height, depth, format, resolvedSamples,
            width > 0 && height > 0 && depth > 0), immutable, levels);
    }
    #endregion

    #region Private
    /// <summary>Names the block widths of the represented explicit compression formats.</summary>
    private static int CompressedImageBlockBytes(TextureFormat format) => format switch
    {
        TextureFormat.CompressedRedRgtc1 or TextureFormat.CompressedSignedRedRgtc1
            or TextureFormat.CompressedR11Eac or TextureFormat.CompressedSignedR11Eac
            or TextureFormat.CompressedRgb8Etc2 or TextureFormat.CompressedSrgb8Etc2
            or TextureFormat.CompressedRgb8PunchthroughAlpha1Etc2 or TextureFormat.CompressedSrgb8PunchthroughAlpha1Etc2
            or TextureFormat.CompressedRgbS3tcDxt1Ext or TextureFormat.CompressedRgbaS3tcDxt1Ext
            or TextureFormat.CompressedSrgbS3tcDxt1Ext or TextureFormat.CompressedSrgbAlphaS3tcDxt1Ext => 8,
        TextureFormat.CompressedRgRgtc2 or TextureFormat.CompressedSignedRgRgtc2
            or TextureFormat.CompressedRg11Eac or TextureFormat.CompressedSignedRg11Eac
            or TextureFormat.CompressedRgba8Etc2Eac or TextureFormat.CompressedSrgb8Alpha8Etc2Eac
            or TextureFormat.CompressedRgbaS3tcDxt3Ext or TextureFormat.CompressedRgbaS3tcDxt5Ext
            or TextureFormat.CompressedSrgbAlphaS3tcDxt3Ext or TextureFormat.CompressedSrgbAlphaS3tcDxt5Ext
            or TextureFormat.CompressedRgbBptcSignedFloat or TextureFormat.CompressedRgbBptcUnsignedFloat
            or TextureFormat.CompressedRgbaBptcUnorm or TextureFormat.CompressedSrgbAlphaBptcUnorm => 16,
        _ => 0
    };

    /// <summary>Resolves selectors from existing binding knowledge and applies only eligible EXT realization.</summary>
    private static GpuResourceMetadataEntry<GpuTextureMetadata>? ResolveObservedTexture(int selector, int nativeTarget,
        int nativeUnit, bool named, bool explicitUnit, bool extNamed = false)
    {
        var manager = RHIResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true) return null;
        int target = nativeTarget;
        if (target >= (int)TextureTarget.TextureCubeMapPositiveX && target <= (int)TextureTarget.TextureCubeMapNegativeZ)
            target = (int)TextureTarget.TextureCubeMap;
        if (target != 0 && !TryImageTarget(target, out _)) return null;
        if (!named)
        {
            int unit = explicitUnit ? nativeUnit - (int)TextureUnit.Texture0 : Current.GetActiveTextureUnit();
            if (!Current.TryGetCachedBoundTexture((TextureTarget)target, unit, out selector))
            {
                // The selector can be unknown after initial admission or a context transition.
                // Resolve that binding alone; resource descriptors never come from this query.
                if (!GpuSupport.HasCachedCapabilitiesForCurrentContext
                    || (uint)unit >= (uint)GpuSupport.Graphics.MaxCombinedTextureImageUnits) return null;
                selector = Current.GetBoundTexture((TextureTarget)target, unit);
            }
        }
        if (extNamed && target != 0)
            manager.RealizeObservedImage(GpuResourceKind.Texture, selector, OpenGLImageValues.Dimension((TextureTarget)target), implicitCreation: true);
        var entry = manager.FindObservedResource(GpuResourceKind.Texture, selector) as GpuResourceMetadataEntry<GpuTextureMetadata>;
        return entry?.IsRealized == true && (target == 0 || entry.Metadata.Target == OpenGLImageValues.Dimension((TextureTarget)target)) ? entry : null;
    }

    /// <summary>Withdraws only the affected original image certification without descriptor discovery.</summary>
    private static void WithdrawImageDefinition(OpenGlStateCache cache, GpuResourceMetadataEntry<GpuTextureMetadata> entry,
        GpuMetadataChange change = GpuMetadataChange.Storage)
    {
        entry.DiscardInitialCandidate();
        cache.InterruptNativeResourceOperation(GpuResourceKind.Texture, (int)entry.Id);
        if (InterruptNativeMetadataCompletion(entry, change)) return;
        entry.Withdraw(change);

    }
    #endregion
}
