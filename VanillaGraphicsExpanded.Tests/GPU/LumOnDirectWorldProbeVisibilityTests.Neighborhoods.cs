using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks neighboring probe ownership, corners, and large-world visibility coordinates.</summary>
public sealed partial class LumOnDirectWorldProbeVisibilityTests
{
    #region Neighborhood Controls
    /// <summary>Blocked red neighbors contribute nothing while visible green neighbors retain full irradiance after ring remapping.</summary>
    [Theory]
    [InlineData(31, 0)]
    [InlineData(-1, 0)]
    [InlineData(-2, 0)]
    [InlineData(31, 1)]
    [InlineData(-1, 1)]
    [InlineData(-2, 1)]
    public void MixedNeighbors_RejectBlockedLighting_AndPreserveVisibleLighting(int consumer, int ringX)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        for (int y = -8; y <= 8; y++)
        for (int z = -8; z <= 8; z++) world.SetBlock(2, y, z, new Block { BlockId = 1 });
        using var local = new LocalTraceVoxelFixture();
        local.Publish(world);
        var atlas = new WorldProbeAtlasData(2, 16);
        for (int y = 0; y < atlas.Height; y++)
        for (int x = 0; x < atlas.Width; x++)
        {
            int physicalX = (x / 16) % 2;
            bool blocked = (physicalX - ringX + 2) % 2 == 1;
            int i = (y * atlas.Width + x) * 4;
            atlas.Radiance[i] = blocked ? 7 : 0;
            atlas.Radiance[i + 1] = blocked ? 0 : 1;
            atlas.Radiance[i + 3] = MathF.Log(65);
        }
        for (int i = 0; i < atlas.Metadata.Length; i += 2) atlas.Metadata[i] = 1;
        var result = RenderDirectVisibility(atlas, local.Scene, new Vector3(1.75f, 0.5f, 0.5f),
            new Vector3(-1.5f), 4, consumer, ring: new Vector3(ringX, 0, 0));
        float expectedGreen = consumer >= 0 ? MathF.PI / (1 + MathF.PI) : MathF.PI;
        for (int i = 0; i < result.Length; i += 4)
        {
            Assert.Equal(0, result[i]);
            Assert.InRange(result[i + 1], expectedGreen - 0.004f, expectedGreen + 0.004f);
            Assert.Equal(0, result[i + 2]);
            if (consumer < 0) Assert.InRange(result[i + 3], 0.65f, 0.73f);
        }
    }

    /// <summary>Diagonal paths through enclosing corner geometry must not admit an exterior probe.</summary>
    [Theory]
    [InlineData(31)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void EnclosingCorner_BlocksExteriorProbe(int consumer)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var local = new LocalTraceVoxelFixture();
        local.Publish(world);
        var center = new Vector3(4.5f, 4.5f, 0.5f);
        var result = RenderDirectVisibility(CreateUniformCache(), local.Scene, new Vector3(0.5f, 0.5f, -5),
            center - new Vector3(16), 32, consumer);
        AssertLighting(result, consumer, false);
    }

    /// <summary>Large signed world origins retain the distinction between a closed wall and an open doorway.</summary>
    [Theory]
    [InlineData(16777216, false)]
    [InlineData(16777216, true)]
    [InlineData(-16777216, false)]
    [InlineData(-16777216, true)]
    public void LargeWorldOrigin_PreservesVisibility(int offset, bool doorway)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((offset - 3, -3, -8), (offset + 3, 3, -2));
        if (doorway) world.SetBlock(offset, 0, -2, null);
        var origin = new VectorInt3(offset, 0, 0);
        using var local = new LocalTraceVoxelFixture(origin);
        local.Publish(world);
        var result = RenderDirectVisibility(CreateUniformCache(), local.Scene, new Vector3(0.5f, 0.5f, -5),
            new Vector3(-7.5f), 16, 31, worldOffset: origin);
        AssertLighting(result, 31, doorway);
    }
    #endregion
}
