using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects exact closed-cell witnesses and finite-segment behavior in the CPU point oracle.</summary>
public sealed class ShadowCandidatePointOracleTests
{
    #region Public API
    /// <summary>A non-binary diagonal crossing a grid corner includes all cells touching that point.</summary>
    [Fact]
    public void NonBinaryCornerCrossingIncludesEveryClosedContact()
    {
        // Arrange: equal binary-double endpoints make the three crossings coincide exactly at the grid corner.
        var domain = new ShadowChunkDomain(0, -1, -1, -1, 2, 2, 2);
        var receiver = Point(.1, .1, .1);
        var source = Point(1.1, 1.1, 1.1);
        var expected = new HashSet<(int, int, int)>();
        for (int z = 0; z <= 1; z++)
        for (int y = 0; y <= 1; y++)
        for (int x = 0; x <= 1; x++) expected.Add((x, y, z));

        // Act: enumerate every cell against exact rational segment constraints.
        var actual = ShadowCandidateOracle.Point(domain, receiver, source);

        // Assert: tangent contacts around the non-binary crossing remain present on all axes.
        Assert.Equal(expected, actual);
    }

    /// <summary>A segment lying on a grid plane includes both sides while remaining clipped to its endpoints.</summary>
    [Fact]
    public void PlaneContactIncludesBothSidesAndFiniteEndpoints()
    {
        // Arrange: x stays on a plane, y crosses one plane, and z stays inside a single cell.
        var domain = new ShadowChunkDomain(0, -1, -1, -1, 3, 3, 3);
        var receiver = Point(1, .125, .375);
        var source = Point(1, 1.125, .375);
        var expected = new HashSet<(int, int, int)>
        {
            (0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0)
        };

        // Act: query the finite receiver-to-source segment in the broad domain.
        var actual = ShadowCandidateOracle.Point(domain, receiver, source);

        // Assert: closed plane contacts are included without extending the segment past its source or receiver.
        Assert.Equal(expected, actual);
    }
    #endregion

    #region Private
    /// <summary>Creates a degenerate closed box at a point endpoint.</summary>
    private static ShadowBounds Point(double x, double y, double z) => new(x, y, z, x, y, z);
    #endregion
}
