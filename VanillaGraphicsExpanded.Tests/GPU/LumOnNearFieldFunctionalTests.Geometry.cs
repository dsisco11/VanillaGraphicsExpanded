using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Near-field geometry and integer-coordinate controls.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Geometry Controls
    /// <summary>Closing a real voxel doorway removes distant lighting while preserving all wall hits.</summary>
    [Fact]
    public void Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++) world.SetBlock(x, y, -2, null);
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);
        var open = Trace(fixture);
        Assert.Contains(open.Radiance.Where((_, i) => i % 4 == 0), value => value > 9);
        Assert.Contains(open.Radiance.Where((_, i) => i % 4 == 0), value => value == 0);
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        fixture.InvalidateAll();
        fixture.Publish(world);
        var closed = Trace(fixture);
        for (int i = 0; i < closed.Radiance.Length; i += 4)
        {
            Assert.Equal(0, closed.Radiance[i]);
            Assert.Equal(1, closed.Meta[i / 2]);
        }
    }

    /// <summary>Integer world-cell mapping retains local occlusion beyond exact single-precision world coordinates.</summary>
    [Theory]
    [InlineData(16777216)]
    [InlineData(-16777216)]
    public void LargeWorldCoordinates_RetainSealedRoomOcclusion(int offset)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld { DefaultLight = Vector4.One };
        world.AddRoom((offset - 3, -3, -8), (offset + 3, 3, -2));
        world.FillLight((offset - 2, -2, -7), (offset + 2, 2, -3), Vector4.Zero);
        var origin = new VectorInt3(offset, 0, 0);
        using var fixture = new NearFieldVoxelFixture(origin);
        fixture.Publish(world);
        var result = Trace(fixture, worldOffset: origin);
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.Equal(0, result.Radiance[i]);
            Assert.Equal(1, result.Meta[i / 2]);
        }
    }

    /// <summary>An initial solid is an immediate near-field hit rather than a clear ray into the cache.</summary>
    [Fact]
    public void InitialSolid_IsNotSkipped()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        // Anchor normal offset straddles X/Y at zero, so cover the neighboring initial cells.
        for (int x = -1; x <= 0; x++)
        for (int y = -1; y <= 0; y++)
            world.SetBlock(x, y, -5, new Vintagestory.API.Common.Block { BlockId = 1 });
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);
        var result = Trace(fixture);
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.Equal(0, result.Radiance[i]);
            Assert.Equal(0, result.Radiance[i + 3]);
            Assert.Equal(1, result.Meta[i / 2]);
        }
    }

    /// <summary>Premature near-field-window exit cannot shorten the required segment and authorize cached light.</summary>
    [Fact]
    public void BoundsExit_RemainsUnresolved()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        AssertUnresolved(Trace(fixture, worldOffset: new VectorInt3(0, 0, 64)));
    }

    /// <summary>The fixed 48-block ring admits complete short segments but never treats an early exit as clear.</summary>
    [Fact]
    public void FixedWindowPreservesCompleteSegmentRequirement()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture(resolution: 48);
        fixture.Publish(new ControlledVoxelWorld());
        var shortSegment = Trace(fixture, cacheSpacing: 2);
        Assert.True(shortSegment.Radiance[0] > 9);
        AssertUnresolved(Trace(fixture, cacheSpacing: 32));
        Assert.Equal(27, fixture.Coordinator.Cells(fixture.Instance).Length);
    }

    #endregion
}
