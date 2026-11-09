
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Checks readiness batching plans exactly the requested physical texels.</summary>
public sealed class SurfaceCacheReadinessPlanningTests
{
    #region Public API
    /// <summary>Adjacent pages merge while gaps, atlas rows, and layers remain separate regions.</summary>
    [Fact]
    public void MergesOnlyContiguousRequestedPagesInSameRowAndLayer()
    {
        // Arrange the unsorted, duplicate pages that span gaps, rows, and layers.
        uint[] requestedPages = [18, 4, 2, 17, 1, 5, 2];

        // Act by planning observation regions for the requested pages.
        var regions = SurfaceCacheReadiness.Plan(requestedPages, 2, 4, 16);

        // Assert that only adjacent requested pages in a shared row and layer are merged.
        Assert.Equal(new SurfaceCacheReadiness.Region[]
        {
            new(0, 0, 4, 2, 0), new(6, 0, 2, 2, 0), new(0, 2, 2, 2, 0), new(0, 0, 4, 2, 1)
        }, regions);
    }

    /// <summary>Every requested tile contributes each pixel once, including a partial atlas layer.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(13)]
    public void PlannedCoverageExactlyMatchesRequestedTiles(int tilesPerAtlas)
    {
        // Arrange requested pages and independently derive the unique physical pixels they cover.
        uint[] requested = [1, 2, 4, 5, 8, 12, 13, 14, 16, 17, 25, 26, 29, 29];
        const int tileSize = 3, tilesPerAxis = 4;
        var expected = new HashSet<(int X, int Y, int Layer)>();
        foreach (uint page in requested)
        {
            int physical = (int)page - 1;
            int local = physical % tilesPerAtlas;
            for (int y = 0; y < tileSize; y++)
            for (int x = 0; x < tileSize; x++)
            {
                expected.Add(((local % tilesPerAxis) * tileSize + x,
                    (local / tilesPerAxis) * tileSize + y, physical / tilesPerAtlas));
            }
        }

        // Act by expanding each planned region into the physical pixels it observes.
        var actual = new HashSet<(int X, int Y, int Layer)>();
        foreach (SurfaceCacheReadiness.Region region in SurfaceCacheReadiness.Plan(requested, tileSize, tilesPerAxis, tilesPerAtlas))
        {
            for (int y = region.Y; y < region.Y + region.Height; y++)
            for (int x = region.X; x < region.X + region.Width; x++)
            {
                Assert.True(actual.Add((x, y, region.Layer)), "A requested pixel must be observed only once.");
            }
        }

        // Assert that planning covers precisely the requested pixels.
        Assert.True(expected.SetEquals(actual));
    }

    /// <summary>Each plan uses the current request set, handles empty input, and rejects invalid pages.</summary>
    [Fact]
    public void ChangedRequestsProduceFreshCoverage()
    {
        // Arrange a request array and create its first plan.
        uint[] requested = [1, 2];
        var first = SurfaceCacheReadiness.Plan(requested, 2, 4, 16);

        // Act by changing the source request and planning again.
        requested[1] = 4;
        var second = SurfaceCacheReadiness.Plan(requested, 2, 4, 16);

        // Assert each result reflects its own request snapshot and invalid input is rejected.
        Assert.Single(first);
        Assert.Equal(2, second.Length);
        Assert.Empty(SurfaceCacheReadiness.Plan([], 2, 4, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceCacheReadiness.Plan([0], 2, 4, 16));
    }
    #endregion
}
