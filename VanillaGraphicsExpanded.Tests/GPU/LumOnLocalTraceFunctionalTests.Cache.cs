using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Distant cache coverage and directional parallax controls.</summary>
public sealed partial class LumOnLocalTraceFunctionalTests
{
    #region Cache Controls
    /// <summary>Near cache hits do not represent the distant domain even when local traversal is clear.</summary>
    [Fact]
    public void ClearScene_RejectsNearCacheHits()
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        AssertUnresolved(Trace(fixture, cacheDistance: 1));
    }

    /// <summary>Directional parallax between eight neighboring centers must preserve a constant incident field.</summary>
    [Fact]
    public void ParallaxNeighborhood_PreservesConstantRadiance()
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        var result = Trace(fixture, cacheResolution: 2);
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.InRange(result.Radiance[i], 9.99f, 10.01f);
            Assert.Equal(1, result.Meta[i / 2]);
        }
    }

    /// <summary>Moving away from a cache center must change a directional lookup while leaving a constant field unchanged.</summary>
    [Fact]
    public void Parallax_ChangesDirectionalLookup()
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        var centered = Trace(fixture, directionalCache: true);
        var displaced = Trace(fixture, directionalCache: true, anchorX: 3);
        int changed = 0;
        for (int i = 0; i < centered.Radiance.Length; i += 4)
        {
            Assert.Equal(1, displaced.Meta[i / 2]);
            if (Math.Abs(centered.Radiance[i] - displaced.Radiance[i]) > 0.03f) changed++;
            Assert.InRange(displaced.Radiance[i + 1], 9.99f, 10.01f);
        }
        Assert.True(changed > 16, "The parallax correction must address different directional cache texels.");
    }

    #endregion
}
