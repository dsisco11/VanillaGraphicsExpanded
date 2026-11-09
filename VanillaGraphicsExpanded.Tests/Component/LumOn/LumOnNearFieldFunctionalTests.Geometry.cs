using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Near-field geometry and integer-coordinate controls.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Public API
    #region Geometry Controls
    /// <summary>Closing a real voxel doorway removes distant lighting while preserving all wall hits.</summary>
    [Fact]
    public void Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections()
    {
        EnsureShaderTestAvailable();
        // Arrange: Open a doorway in a real voxel room and publish it for the near-field trace.
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++) world.SetBlock(x, y, -2, null);
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);

        // Act and assert: Render the open doorway and verify both expected directional witnesses.
        var open = Trace(fixture);
        Assert.True(ObserveTrace(open, SurfacePredicates.LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections).MatchingTexels>0);
        Assert.True(ObserveTrace(open, SurfacePredicates.LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections_2).MatchingTexels>0);
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        fixture.InvalidateAll();
        fixture.Publish(world);

        // Act and assert: Render again after publication; closing the doorway removes cache light but keeps wall hits.
        var closed = Trace(fixture);
        ObserveTrace(closed, SurfacePredicates.LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections_3).AssertEqual();
    }

    /// <summary>Integer world-cell mapping retains local occlusion beyond exact single-precision world coordinates.</summary>
    [Theory]
    [InlineData(16777216)]
    [InlineData(-16777216)]
    public void LargeWorldCoordinates_RetainSealedRoomOcclusion(int offset)
    {
        EnsureShaderTestAvailable();
        // Arrange: Place the same sealed room at the signed integer origin under test.
        var world = new ControlledVoxelWorld { DefaultLight = Vector4.One };
        world.AddRoom((offset - 3, -3, -8), (offset + 3, 3, -2));
        world.FillLight((offset - 2, -2, -7), (offset + 2, 2, -3), Vector4.Zero);
        var origin = new VectorInt3(offset, 0, 0);
        using var fixture = new NearFieldVoxelFixture(origin);
        fixture.Publish(world);

        // Act and assert: Trace using split integer coordinates and retain the room's occlusion.
        var result = Trace(fixture, worldOffset: origin);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsGeometry_Doorway_OpenThenClosed_ChangesOnlyUnoccludedDirections_3).AssertEqual();
    }

    /// <summary>An initial solid is an immediate near-field hit rather than a clear ray into the cache.</summary>
    [Fact]
    public void InitialSolid_IsNotSkipped()
    {
        EnsureShaderTestAvailable();
        // Arrange: Cover every cell touched by the anchor's normal offset at the initial ray position.
        var world = new ControlledVoxelWorld();
        // Anchor normal offset straddles X/Y at zero, so cover the neighboring initial cells.
        for (int x = -1; x <= 0; x++)
        for (int y = -1; y <= 0; y++)
            world.SetBlock(x, y, -5, new Vintagestory.API.Common.Block { BlockId = 1 });
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);

        // Act and assert: Trace from the solid starting point and require an immediate near-field hit.
        var result = Trace(fixture);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsGeometry_InitialSolid_IsNotSkipped).AssertEqual();
    }

    /// <summary>Premature near-field-window exit cannot shorten the required segment and authorize cached light.</summary>
    [Fact]
    public void BoundsExit_RemainsUnresolved()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish a clear scene, then place the query beyond the finite near-field window.
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());

        // Act and assert: The production trace cannot treat its early window exit as a complete clear segment.
        AssertUnresolved(Trace(fixture, worldOffset: new VectorInt3(0, 0, 64)));
    }

    /// <summary>The fixed 48-block ring admits complete short segments but never treats an early exit as clear.</summary>
    [Fact]
    public void FixedWindowPreservesCompleteSegmentRequirement()
    {
        EnsureShaderTestAvailable();
        // Arrange: Use the fixed 48-block ring and publish a clear local region.
        using var fixture = new NearFieldVoxelFixture(resolution: 48);
        fixture.Publish(new ControlledVoxelWorld());

        // Act and assert: A short segment reaches the cache; a longer segment exceeding the window stays unresolved.
        var shortSegment = Trace(fixture, cacheSpacing: 2);
        Assert.True(TraceRed(shortSegment) > 9);
        AssertUnresolved(Trace(fixture, cacheSpacing: 32));
        Assert.Equal(27, fixture.Coordinator.Cells(fixture.Instance).Length);
    }

    #endregion
    #endregion
}
