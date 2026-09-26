using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Plans exact requested-tile readback regions without including unused atlas texels.</summary>
internal static class SurfaceCacheReadinessReadback
{
    /// <summary>Describes one contiguous requested tile run in a single atlas layer and row.</summary>
    internal readonly record struct Region(int X, int Y, int Width, int Height, int Layer);

    #region Region planning
    /// <summary>Coalesces sorted, distinct one-based physical pages without crossing row, layer, or unrequested gaps.</summary>
    internal static ImmutableArray<Region> Plan(IEnumerable<uint> physicalPages, int tileSize, int tilesPerAxis, int tilesPerAtlas)
    {
        ArgumentNullException.ThrowIfNull(physicalPages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tilesPerAxis);
        if (tilesPerAtlas <= 0 || tilesPerAtlas > (long)tilesPerAxis * tilesPerAxis)
            throw new ArgumentOutOfRangeException(nameof(tilesPerAtlas));
        var regions = ImmutableArray.CreateBuilder<Region>();
        foreach (uint page in physicalPages.Distinct().Order())
        {
            if (page == 0) throw new ArgumentOutOfRangeException(nameof(physicalPages), "Physical page zero is reserved.");
            int physical = checked((int)(page - 1));
            int local = physical % tilesPerAtlas;
            var next = new Region(checked((local % tilesPerAxis) * tileSize),
                checked((local / tilesPerAxis) * tileSize), tileSize, tileSize, physical / tilesPerAtlas);
            // Sorting makes adjacent regions mergeable only when every intervening physical tile was requested.
            if (regions.Count > 0 && regions[^1] is var previous && previous.Layer == next.Layer &&
                previous.Y == next.Y && checked(previous.X + previous.Width) == next.X)
                regions[^1] = previous with { Width = checked(previous.Width + tileSize) };
            else regions.Add(next);
        }
        return regions.ToImmutable();
    }

    /// <summary>Requires initialized alpha for every texel; rejects empty or incomplete RGBA observations.</summary>
    internal static bool IsFullyInitialized(ReadOnlySpan<float> pixels)
    {
        if (pixels.IsEmpty || (pixels.Length & 3) != 0) return false;
        for (int channel = 3; channel < pixels.Length; channel += 4)
            if (pixels[channel] != 1) return false;
        return true;
    }
    #endregion

    #region Initialized observation
    /// <summary>Stages fresh requested regions together and checks every outgoing texel with one mapped observation.</summary>
    internal static bool AllInitialized(Texture3D texture, ImmutableArray<Region> regions)
    {
        if (regions.IsDefaultOrEmpty) return false;
        int count = 0;
        foreach (var region in regions)
            count = checked(count + checked((int)((long)region.Width * region.Height << 2)));
        using var framebuffer = GpuFramebuffer.CreateEmpty("Test_SurfaceCacheReadiness");
        using var draw = GlStateCache.Current.BindFramebufferScope(FramebufferTarget.DrawFramebuffer, framebuffer.FboId);
        using var read = GlStateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, framebuffer.FboId);
        using var staging = GpuPixelPackBuffer.Create(debugName: "Test_SurfaceCacheReadiness");
        staging.AllocateOrphan(checked((int)((long)count << 2)));
        int layer = -1, offsetBytes = 0;
        // Queue exact runs into disjoint staging ranges, then synchronize only once when mapping.
        foreach (var region in regions)
        {
            if (layer != region.Layer)
            {
                framebuffer.AttachColorLayer(texture, region.Layer);
                if (!framebuffer.CheckStatus(out string? error)) throw new InvalidOperationException(error);
                framebuffer.Bind();
                layer = region.Layer;
            }
            staging.ReadPixels(region.X, region.Y, region.Width, region.Height, PixelFormat.Rgba, PixelType.Float, offsetBytes);
            offsetBytes = checked(offsetBytes + checked((int)((long)region.Width * region.Height << 4)));
        }
        using var mapped = staging.MapRange<float>(0, count, MapBufferAccessMask.MapReadBit);
        if (!mapped.IsMapped) throw new InvalidOperationException("Unable to map requested Surface Cache texels.");
        return IsFullyInitialized(mapped.Span);
    }
    #endregion
}
