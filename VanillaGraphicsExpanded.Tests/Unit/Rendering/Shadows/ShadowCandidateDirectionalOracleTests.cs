using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects directional oracle rejection of geometrically impossible neighbor cells.</summary>
public sealed class ShadowCandidateDirectionalOracleTests
{
    #region Public API
    /// <summary>A narrow positive-X cap excludes transverse neighbors while retaining receiver and forward cells.</summary>
    [Fact]
    public void NarrowCapRejectsCellsOutsideItsMinimumAngularReach()
    {
        // Arrange: transverse neighbors need at least 0.2 lateral travel but at most 0.8 forward travel.
        var domain = new ShadowChunkDomain(0, -1, -1, -1, 8, 1, 1);
        var receiver = new ShadowBounds(.2, .2, .2, .8, .8, .8);

        // Act: conservatively enumerate the positive-X cap with an independent interval branch bound.
        var candidates = ShadowCandidateOracle.Directional(domain, receiver, 1, 0, 0, 8);

        // Assert: all analytically impossible transverse neighbors are rejected; overlap and interior remain.
        Assert.DoesNotContain((0, -1, -1), candidates);
        Assert.DoesNotContain((0, 0, -1), candidates);
        Assert.DoesNotContain((0, 1, -1), candidates);
        Assert.DoesNotContain((0, -1, 0), candidates);
        Assert.DoesNotContain((0, 1, 0), candidates);
        Assert.DoesNotContain((0, -1, 1), candidates);
        Assert.DoesNotContain((0, 0, 1), candidates);
        Assert.DoesNotContain((0, 1, 1), candidates);
        Assert.Contains((0, 0, 0), candidates);
        Assert.Contains((1, 0, 0), candidates);
    }

    /// <summary>A cap tangent to a closed receiver-neighbor corner remains conservatively included.</summary>
    [Fact]
    public void TangentAtClosedNeighborBoundaryRemainsIncluded()
    {
        // Arrange: the limiting direction uses the farthest forward and nearest transverse points.
        var domain = new ShadowChunkDomain(0, 0, -1, 0, 0, -1, 0);
        var receiver = new ShadowBounds(.2, .2, .2, .8, .8, .8);
        double tangentDegrees = Math.Atan(.2 / .8) * 180 / Math.PI;

        // Act: query exactly at the analytically derived angular reach of the closed cell corner.
        var candidates = ShadowCandidateOracle.Directional(domain, receiver, 1, 0, 0, tangentDegrees);

        // Assert: numerically ambiguous equality at a closed cap boundary is retained.
        Assert.Contains((0, -1, 0), candidates);
    }
    #endregion
}
