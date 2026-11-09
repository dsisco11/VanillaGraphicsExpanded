using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Local surface-lighting and emission controls.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Public API
    #region Lighting Controls
    /// <summary>Raw material emission cannot replace missing cached outgoing radiance.</summary>
    [Fact]
    public void RawEmission_RemainsUnavailableWithoutSurfaceCache()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish an opaque room with material emission but no outgoing surface-lighting cache.
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world, new Vector4(0.5f, 0.25f, 0.125f, 2));

        // Act and assert: Trace with emission boosting and require lighting to remain unavailable.
        var result = Trace(fixture, suppress: true, emissionBoost: 3);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsLighting_RawEmission_RemainsUnavailableWithoutSurfaceCache).AssertEqual();
    }

    /// <summary>A known wall with missing lighting must remain an opaque hit, never a cache lookup.</summary>
    [Fact]
    public void MissingMaterial_PreservesOpaqueHit()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish a sealed room whose opaque blocks have unresolved material identities.
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world, materialIdentity: 0);

        // Act and assert: Trace the wall and distinguish its opaque hit from a valid cache lookup.
        var result = Trace(fixture);
        AssertUnresolved(result);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsLighting_MissingMaterial_PreservesOpaqueHit).AssertEqual();
    }

    /// <summary>Raw sunlight cannot bypass surface-cache publication, even on an upward face.</summary>
    [Fact]
    public void UpwardSurface_RequiresPublishedSurfaceLighting()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish a broad upward-facing plane with raw sunlight but no surface-lighting page.
        var world = new ControlledVoxelWorld { DefaultLight = new Vector4(0, 0, 0, 1) };
        for (int z = -32; z < 32; z++)
        for (int x = -32; x < 32; x++)
            world.SetBlock(x, -3, z, new Vintagestory.API.Common.Block { BlockId = 1 });
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);

        // Act: Trace the production shader across the upward-facing plane.
        var result = Trace(fixture);

        // Assert: Its independent observation proves the hit-lighting path and the expected unresolved result.
        var observation = ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsLighting_UpwardSurface_RequiresPublishedSurfaceLighting);
        observation.AssertEqual();
        uint hitCount = observation.MatchingTexels;
        Assert.True(hitCount > 32, "The production rays must exercise the plane's hit-lighting path.");
    }

    /// <summary>Game voxel light levels do not silently substitute for unavailable outgoing radiance.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void RawVoxelLightingDoesNotSubstituteForMissingCache(float intensity)
    {
        EnsureShaderTestAvailable();
        // Arrange: Vary raw voxel light while keeping the room and absent outgoing cache fixed.
        var world = new ControlledVoxelWorld { DefaultLight = new Vector4(intensity, 0, 1, 0) };
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);

        // Act and assert: Trace the room and verify raw voxel light never substitutes for cached radiance.
        var result = Trace(fixture);

        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsLighting_RawEmission_RemainsUnavailableWithoutSurfaceCache).AssertEqual();
    }

    #endregion
    #endregion
}
