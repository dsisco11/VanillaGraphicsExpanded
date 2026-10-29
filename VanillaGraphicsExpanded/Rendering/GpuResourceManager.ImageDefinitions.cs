using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Builds complete texture image snapshots from established storage and generation facts.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Replaces the selected definition or derives the entire immutable image chain.</summary>
    internal void PublishTextureDefinition(GpuResourceMetadataEntry<GpuTextureMetadata> entry,
        GpuTextureLevelMetadata image, bool immutable, int levels)
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        var prior = entry.Metadata;
        var images = immutable ? ImmutableArray.CreateBuilder<GpuTextureLevelMetadata>() : prior.Levels.ToBuilder();
        int faces = immutable && prior.Target == TextureDimension.Cube ? 6 : 1;
        for (int level = 0; level < (immutable ? levels : 1); level++)
        {
            int width = immutable ? Math.Max(1, image.Width >> level) : image.Width;
            int height = immutable && prior.Target != TextureDimension.OneArray ? Math.Max(1, image.Height >> level) : image.Height;
            int depth = immutable && prior.Target == TextureDimension.Three ? Math.Max(1, image.Depth >> level) : image.Depth;
            for (int face = 0; face < faces; face++)
                ReplaceImage(images, image with { Level = immutable ? level : image.Level,
                    Face = faces == 6 ? CubeFace.PositiveX + face : image.Face, Width = width, Height = height, Depth = depth });
        }
        var candidate = prior with { Levels = images.ToImmutable(), ImmutableStorage = immutable || prior.ImmutableStorage,
            ImmutableLevels = immutable ? levels : prior.ImmutableLevels };
        if (immutable)
            candidate = candidate with { View = new(0, levels, 0, prior.Target == TextureDimension.Cube ? 6
                : prior.Target == TextureDimension.OneArray ? image.Height
                : prior.Target is TextureDimension.TwoArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray ? image.Depth : 1) };
        PublishMetadata(entry, candidate, GpuMetadataChange.Storage);
    }

    /// <summary>Builds changed mutable mip descriptors before observation interrupts any existing operation.</summary>
    internal static GpuTextureMetadata? BuildGeneratedTextureMetadata(in GpuTextureMetadata data)
    {
        if (data.ImmutableStorage || data.Levels.IsDefault) return null;
        var bases = ImmutableArray.CreateBuilder<GpuTextureLevelMetadata>();
        foreach (var image in data.Levels)
            if (image.Level == data.BaseLevel && image.Allocated) bases.Add(image);
        if (bases.Count != (data.Target == TextureDimension.Cube ? 6 : 1)) return null;
        var first = bases[0];
        if (data.Target is TextureDimension.Cube or TextureDimension.CubeArray
            && (first.Width != first.Height || first.Width <= 0 || first.Depth <= 0
                || data.Target == TextureDimension.CubeArray && first.Depth % 6 != 0)) return null;
        foreach (var image in bases)
            if (image.Width != first.Width || image.Height != first.Height || image.Depth != first.Depth || image.Format != first.Format) return null;
        int extent = Math.Max(first.Width, data.Target == TextureDimension.OneArray ? 1 : first.Height);
        if (data.Target == TextureDimension.Three) extent = Math.Max(extent, first.Depth);
        int last = data.BaseLevel;
        while ((extent >>= 1) > 0) last++;
        last = Math.Min(last, data.MaxLevel);
        var images = data.Levels.ToBuilder();
        bool changed = false;
        foreach (var image in bases)
            for (int level = data.BaseLevel + 1; level <= last; level++)
            {
                int shift = level - data.BaseLevel;
                changed |= ReplaceImage(images, image with { Level = level, Width = Math.Max(1, image.Width >> shift),
                    Height = data.Target == TextureDimension.OneArray ? image.Height : Math.Max(1, image.Height >> shift),
                    Depth = data.Target == TextureDimension.Three ? Math.Max(1, image.Depth >> shift) : image.Depth });
            }
        return changed ? data with { Levels = images.ToImmutable() } : null;
    }
    #endregion

    #region Private
    /// <summary>Updates one image in place and reports whether its allocation descriptor changed.</summary>
    private static bool ReplaceImage(ImmutableArray<GpuTextureLevelMetadata>.Builder images, GpuTextureLevelMetadata candidate)
    {
        for (int i = 0; i < images.Count; i++)
            if (images[i].Level == candidate.Level && images[i].Face == candidate.Face)
            {
                bool changed = images[i] != candidate;
                if (candidate.Allocated) images[i] = candidate;
                else images.RemoveAt(i);
                return changed;
            }
        if (!candidate.Allocated) return false;
        images.Add(candidate);
        return true;
    }
    #endregion
}
