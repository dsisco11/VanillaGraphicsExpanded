using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Local surface-lighting and emission controls.</summary>
public sealed partial class LumOnLocalTraceFunctionalTests
{
    #region Lighting Controls
    /// <summary>Emission is evaluated once with the GI boost and survives cache-only suppression.</summary>
    [Fact]
    public void LocalEmission_IsIndependentOfCacheSuppression()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Publish(world, new Vector4(0.5f, 0.25f, 0.125f, 2));
        var result = Trace(fixture, suppress: true, emissionBoost: 3);
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.InRange(result.Radiance[i], 2.99f, 3.01f);
            Assert.InRange(result.Radiance[i + 1], 1.49f, 1.51f);
            Assert.InRange(result.Radiance[i + 2], 0.74f, 0.76f);
            Assert.Equal(1, result.Meta[i / 2]);
        }
    }

    /// <summary>A known wall with missing lighting must remain an opaque hit, never a cache lookup.</summary>
    [Fact]
    public void MissingMaterial_PreservesOpaqueHit()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Publish(world, materialIdentity: 0);
        var result = Trace(fixture);
        AssertUnresolved(result);
        for (int i = 1; i < result.Meta.Length; i += 2)
        {
            Assert.NotEqual(0u, Flags(result.Meta[i]) & 1u);
            Assert.Equal(0u, Flags(result.Meta[i]) & (1u << 5));
        }
    }

    /// <summary>Fully visible sky over a diffuse horizontal plane matches the CPU model's normalized inverse-pi term.</summary>
    [Fact]
    public void UpwardSurface_SkyTermMatchesNormalizedCpuModel()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld { DefaultLight = new Vector4(0, 0, 0, 1) };
        for (int z = -32; z < 32; z++)
        for (int x = -32; x < 32; x++)
            world.SetBlock(x, -3, z, new Vintagestory.API.Common.Block { BlockId = 1 });
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Publish(world);
        var result = Trace(fixture);
        int hitCount = 0;
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            if ((Flags(result.Meta[i / 2 + 1]) & 1u) == 0) continue;
            hitCount++;
            Assert.InRange(result.Radiance[i], 1 / MathF.PI - 0.002f, 1 / MathF.PI + 0.002f);
            Assert.Equal(1, result.Meta[i / 2]);
        }
        Assert.True(hitCount > 32, "The production rays must exercise the plane's hit-lighting path.");
    }

    #endregion
}
