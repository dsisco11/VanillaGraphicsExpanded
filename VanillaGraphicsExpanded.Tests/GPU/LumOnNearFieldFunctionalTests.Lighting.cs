using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Local surface-lighting and emission controls.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Lighting Controls
    /// <summary>Raw material emission cannot replace missing cached outgoing radiance.</summary>
    [Fact]
    public void RawEmission_RemainsUnavailableWithoutSurfaceCache()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world, new Vector4(0.5f, 0.25f, 0.125f, 2));
        var result = Trace(fixture, suppress: true, emissionBoost: 3);
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.Equal(0,result.Radiance[i]);
            Assert.Equal(0,result.Radiance[i+1]);
            Assert.Equal(0,result.Radiance[i+2]);
            Assert.Equal(0, result.Meta[i / 2]);
        }
    }

    /// <summary>A known wall with missing lighting must remain an opaque hit, never a cache lookup.</summary>
    [Fact]
    public void MissingMaterial_PreservesOpaqueHit()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world, materialIdentity: 0);
        var result = Trace(fixture);
        AssertUnresolved(result);
        for (int i = 1; i < result.Meta.Length; i += 2)
        {
            Assert.NotEqual(0u, Flags(result.Meta[i]) & 1u);
            Assert.Equal(0u, Flags(result.Meta[i]) & (1u << 5));
        }
    }

    /// <summary>Raw sunlight cannot bypass surface-cache publication, even on an upward face.</summary>
    [Fact]
    public void UpwardSurface_RequiresPublishedSurfaceLighting()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld { DefaultLight = new Vector4(0, 0, 0, 1) };
        for (int z = -32; z < 32; z++)
        for (int x = -32; x < 32; x++)
            world.SetBlock(x, -3, z, new Vintagestory.API.Common.Block { BlockId = 1 });
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);
        var result = Trace(fixture);
        int hitCount = 0;
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            if ((Flags(result.Meta[i / 2 + 1]) & 1u) == 0) continue;
            hitCount++;
            Assert.Equal(0,result.Radiance[i]);
            Assert.Equal(0, result.Meta[i / 2]);
        }
        Assert.True(hitCount > 32, "The production rays must exercise the plane's hit-lighting path.");
    }

    /// <summary>Game voxel light levels do not silently substitute for unavailable outgoing radiance.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.001f)]
    [InlineData(0.004f)]
    [InlineData(0.025f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void RawVoxelLightingDoesNotSubstituteForMissingCache(float intensity)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld { DefaultLight = new Vector4(intensity, 0, 1, 0) };
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);
        var result = Trace(fixture);

        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.Equal(0,result.Radiance[i]);

            Assert.Equal(0, result.Radiance[i + 1]);
            Assert.Equal(0, result.Radiance[i + 2]);
            Assert.Equal(0, result.Meta[i / 2]);
        }
    }

    #endregion
}
