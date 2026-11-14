using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects conservative chunk-relative coverage at large and negative world positions.</summary>
public sealed class ShadowCoverageBoundsTests
{
    #region Public API
    /// <summary>Canonical coverage expands each endpoint outward and remains stable when reused.</summary>
    [Theory]
    [InlineData(1_000_000_000.1, -1_000_000_000.1, 31.9999999)]
    [InlineData(-68_719_476_608.25, 68_719_476_607.75, -32.0000001)]
    public void CanonicalBoundsContainOriginalEndpointsAndAreIdempotent(double x, double y, double z)
    {
        // Arrange: place a tight enclosure across chunk boundaries at large positive and negative coordinates.
        var original = new ShadowBounds(x, y, z, x + 0.2, y + 0.4, z + 0.6);

        // Act: convert it to the integer-chunk plus local-FP32 representation used by GPU certificates.
        var canonical = ShadowCoverageBounds.Canonicalize(original);
        var canonicalAgain = ShadowCoverageBounds.Canonicalize(canonical);

        // Assert: outward rounding preserves strict coverage while repeated publication does not keep growing it.
        Assert.True(canonical.Contains(original));
        Assert.Equal(canonical, canonicalAgain);
    }

    /// <summary>Page requests expose the expanded region that both selection and the GPU certificate consume.</summary>
    [Fact]
    public void PageRegionUsesCanonicalCoverageAfterConstructionAndWithUpdates()
    {
        // Arrange: create a page with a tight enclosure at large negative world coordinates.
        var source = new ShadowSourceKey(1, 0, ShadowSourceKind.Sun, 2, 3, 0);
        var initial = new ShadowBounds(-1_000_000_000.1, 10, 20, -999_999_999.9, 10.25, 20.5);
        var request = new ShadowPageRequest(ShadowPageOperation.Lookup,
            new(source, 1, 0, 0, 0), initial, 1, 1, 0);

        // Act: replace the region through record `with`, the same path used while changing page operations.
        var updated = request with { Region = new(-1_000_000_100.1, 11, 21, -1_000_000_099.9, 11.25, 21.5) };

        // Assert: CPU consumers observe conservative enclosures on both original and updated requests.
        Assert.True(request.Region.Contains(initial));
        Assert.True(updated.Region.Contains(new ShadowBounds(-1_000_000_100.1, 11, 21,
            -1_000_000_099.9, 11.25, 21.5)));
        Assert.False(request.Region.Contains(updated.Region));
    }
    #endregion
}
