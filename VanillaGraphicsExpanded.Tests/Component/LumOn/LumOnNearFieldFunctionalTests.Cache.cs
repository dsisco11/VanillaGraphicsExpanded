using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Distant cache coverage and directional parallax controls.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Public API
    #region Cache Controls
    /// <summary>Near cache hits do not represent the distant domain even when local traversal is clear.</summary>
    [Fact]
    public void ClearScene_RejectsNearCacheHits()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish an empty local world whose cached sample is inside the near-field exclusion radius.
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());

        // Act and assert: Trace toward that near cache sample and require it to remain unavailable.
        AssertUnresolved(Trace(fixture, cacheDistance: 1));
    }

    /// <summary>Directional parallax between eight neighboring centers must preserve a constant incident field.</summary>
    [Fact]
    public void ParallaxNeighborhood_PreservesConstantRadiance()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish an empty scene and use the eight-center directional cache fixture.
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());

        // Act and assert: Trace the GPU shader across the neighborhood and require constant input radiance to be preserved.
        var result = Trace(fixture, cacheResolution: 2);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsCache_ParallaxNeighborhood_PreservesConstantRadiance).AssertEqual();
    }

    /// <summary>Moving away from a cache center must change a directional lookup while leaving a constant field unchanged.</summary>
    [Fact]
    public void Parallax_ChangesDirectionalLookup()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish the same directional cache at its center and a displaced anchor.
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());

        // Act: Render both anchors and compare their selected directional samples.
        var centered = Trace(fixture, directionalCache: true);
        var displaced = Trace(fixture, directionalCache: true, anchorX: 3);

        // Assert: The independent observation sees the expected parallax difference across enough texels.
        var comparison = ObserveTrace(displaced, SurfacePredicates.LumOnNearFieldFunctionalTestsCache_Parallax_ChangesDirectionalLookup, centered);
        comparison.AssertEqual();
        uint changed = comparison.MatchingTexels;
        Assert.True(changed > 16, "The parallax correction must address different directional cache texels.");
    }

    #endregion
    #endregion
}
