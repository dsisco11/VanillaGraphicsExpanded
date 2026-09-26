using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks that readiness batching observes exactly the requested physical texels.</summary>
public sealed class SurfaceCacheReadinessReadbackPlanningTests
{
    #region Physical region coverage
    /// <summary>Adjacent requested pages merge while gaps, atlas rows and atlas layers remain separate.</summary>
    [Fact]
    public void MergesOnlyContiguousRequestedPagesInSameRowAndLayer()
    {
        var regions = SurfaceCacheReadinessReadback.Plan([18, 4, 2, 17, 1, 5, 2], 2, 4, 16);
        Assert.Equal(new SurfaceCacheReadinessReadback.Region[]
        {
            new(0, 0, 4, 2, 0), new(6, 0, 2, 2, 0), new(0, 2, 2, 2, 0), new(0, 0, 4, 2, 1)
        }, regions);
    }

    /// <summary>Every requested tile contributes all pixels exactly once with no extra pixels, including partial atlas rows.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(13)]
    public void PlannedCoverageExactlyMatchesRequestedTiles(int tilesPerAtlas)
    {
        uint[] requested = [1, 2, 4, 5, 8, 12, 13, 14, 16, 17, 25, 26, 29, 29];
        const int tileSize = 3, tilesPerAxis = 4;
        var expected = new HashSet<(int X, int Y, int Layer)>();
        foreach (uint page in requested)
        {
            int physical = (int)page - 1, local = physical % tilesPerAtlas;
            for (int y = 0; y < tileSize; y++)
            for (int x = 0; x < tileSize; x++)
                expected.Add(((local % tilesPerAxis) * tileSize + x,
                    (local / tilesPerAxis) * tileSize + y, physical / tilesPerAtlas));
        }
        var actual = new HashSet<(int X, int Y, int Layer)>();
        foreach (var region in SurfaceCacheReadinessReadback.Plan(requested, tileSize, tilesPerAxis, tilesPerAtlas))
            for (int y = region.Y; y < region.Y + region.Height; y++)
            for (int x = region.X; x < region.X + region.Width; x++)
                Assert.True(actual.Add((x, y, region.Layer)), "A requested pixel must be observed only once.");
        Assert.True(expected.SetEquals(actual));
    }

    /// <summary>Each call plans the current requested set rather than preserving pages from an earlier snapshot.</summary>
    [Fact]
    public void ChangedRequestsProduceFreshCoverage()
    {
        uint[] requested = [1, 2];
        var first = SurfaceCacheReadinessReadback.Plan(requested, 2, 4, 16);
        requested[1] = 4;
        var second = SurfaceCacheReadinessReadback.Plan(requested, 2, 4, 16);
        Assert.Single(first);
        Assert.Equal(2, second.Length);
        Assert.Empty(SurfaceCacheReadinessReadback.Plan([], 2, 4, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceCacheReadinessReadback.Plan([0], 2, 4, 16));
    }
    #endregion

    #region Complete alpha domain
    /// <summary>Uninitialized interior and final texels cannot hide behind neighboring initialized tiles.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void EveryRequestedTexelMustHaveInitializedAlpha(int texel)
    {
        var pixels = new float[16 << 2];
        for (int index = 3; index < pixels.Length; index += 4) pixels[index] = 1;
        Assert.True(SurfaceCacheReadinessReadback.IsFullyInitialized(pixels));
        pixels[(texel << 2) + 3] = 0;
        Assert.False(SurfaceCacheReadinessReadback.IsFullyInitialized(pixels));
        pixels[(texel << 2) + 3] = float.NaN;
        Assert.False(SurfaceCacheReadinessReadback.IsFullyInitialized(pixels));
        Assert.False(SurfaceCacheReadinessReadback.IsFullyInitialized([]));
    }
    #endregion
}
