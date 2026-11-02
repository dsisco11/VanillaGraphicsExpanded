using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Distant cache coverage and directional parallax controls.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Cache Controls
    /// <summary>Near cache hits do not represent the distant domain even when local traversal is clear.</summary>
    [Fact]
    public void ClearScene_RejectsNearCacheHits()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        AssertUnresolved(Trace(fixture, cacheDistance: 1));
    }

    /// <summary>Directional parallax between eight neighboring centers must preserve a constant incident field.</summary>
    [Fact]
    public void ParallaxNeighborhood_PreservesConstantRadiance()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        var result = Trace(fixture, cacheResolution: 2);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsCache_ParallaxNeighborhood_PreservesConstantRadiance).AssertEqual();
    }

    /// <summary>Moving away from a cache center must change a directional lookup while leaving a constant field unchanged.</summary>
    [Fact]
    public void Parallax_ChangesDirectionalLookup()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        var centered = Trace(fixture, directionalCache: true);
        var displaced = Trace(fixture, directionalCache: true, anchorX: 3);

        var comparison = ObserveTrace(displaced, SurfacePredicates.LumOnNearFieldFunctionalTestsCache_Parallax_ChangesDirectionalLookup, centered);
        comparison.AssertEqual();
        uint changed = comparison.MatchingTexels;
        Assert.True(changed > 16, "The parallax correction must address different directional cache texels.");
    }

    #endregion
}
