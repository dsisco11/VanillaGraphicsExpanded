using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Characterizes the spatial mapping and lifetime barriers for a possible probe partition.</summary>
public sealed class WorldProbePartitionEvaluationTests
{
    #region Spatial equivalence
    /// <summary>Both even and odd lattices fit fixed cells when sampling offsets remain consumer-owned.</summary>
    [Theory]
    [InlineData(8, 1.5, -0.01)]
    [InlineData(9, 1.5, -0.01)]
    [InlineData(20, 3.0, 1000000.25)]
    [InlineData(21, 6.0, -1000000.25)]
    public void ProbePositions_MapToWorldZeroCells_WithoutChangingSampling(int resolution, double spacing, double camera)
    {
        var scheduler = new LumOnWorldProbeScheduler(1, resolution);
        scheduler.UpdateOrigins(new(camera, camera, camera), spacing);
        Assert.True(scheduler.TryGetLevelParams(0, out var origin, out _));
        var layout = new PartitionLayout(new(spacing, spacing, spacing));
        var keys = new HashSet<PartitionCoordinate>();
        double offset = resolution % 2 == 0 ? 0.5 : 0.0;

        // Logical cells are identified by their sample, not by copying an odd grid's half-cell origin.
        for (int z = 0; z < resolution; z++)
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            var sample = LumOnClipmapTopology.IndexToProbeCenterWorld(new(x, y, z), origin, spacing);
            var key = layout.Coordinate(new(sample.X, sample.Y, sample.Z));
            Assert.True(keys.Add(key));
            var bounds = layout.Bounds(key);
            Assert.Equal(sample.X, bounds.Min.X + offset * spacing, 8);
            Assert.Equal(sample.Y, bounds.Min.Y + offset * spacing, 8);
            Assert.Equal(sample.Z, bounds.Min.Z + offset * spacing, 8);
        }
        Assert.Equal(resolution * resolution * resolution, keys.Count);
    }

    /// <summary>Naively passing an odd lattice's existing volume to the coordinator over-selects cells.</summary>
    [Fact]
    public void OddResolution_OriginalVolumeBoundsAreNotTheLogicalCellEnvelope()
    {
        const int resolution = 9;
        const double spacing = 1.5;
        var origin = LumOnClipmapTopology.GetOriginMinCorner(new(0, 0, 0), spacing, resolution);
        var layout = new PartitionLayout(new(spacing, spacing, spacing));
        var bounds = new PartitionBounds(new(origin.X, origin.Y, origin.Z),
            new(origin.X + resolution * spacing, origin.Y + resolution * spacing, origin.Z + resolution * spacing));
        Assert.Equal(1000, layout.Intersecting(bounds).Count());
        Assert.Equal(729, resolution * resolution * resolution);
    }
    #endregion

    #region Existing lifetime limitations
    /// <summary>Records the existing teleport defect; this is a characterization, not a safety assertion.</summary>
    [Fact]
    public void Teleport_LateSuccessfulCompletionCurrentlyMarksReassignedSlotValid()
    {
        const int resolution = 8;
        const double spacing = 4;
        var scheduler = new LumOnWorldProbeScheduler(1, resolution);
        scheduler.UpdateOrigins(new(0, 0, 0), spacing);
        var request = Assert.Single(scheduler.BuildUpdateList(1, new(0, 0, 0), spacing, [1], 1, int.MaxValue, 1));
        Assert.True(scheduler.TryClaim(request, 1));
        Assert.True(scheduler.TryGetLevelParams(0, out var oldOrigin, out _));
        var oldPosition = LumOnClipmapTopology.IndexToProbeCenterWorld(request.LocalIndex, oldOrigin, spacing);

        // An exact whole-window jump reuses the same storage index for a different world position.
        scheduler.UpdateOrigins(new(resolution * spacing, 0, 0), spacing);
        Assert.True(scheduler.TryGetLevelParams(0, out var newOrigin, out _));
        var newPosition = LumOnClipmapTopology.IndexToProbeCenterWorld(request.LocalIndex, newOrigin, spacing);
        Assert.Equal(resolution * spacing, newPosition.X - oldPosition.X);
        var states = new LumOnWorldProbeLifecycleState[scheduler.ProbesPerLevel];
        Assert.True(scheduler.TryCopyLifecycleStates(0, states));
        Assert.Equal(LumOnWorldProbeLifecycleState.Dirty, states[request.StorageLinearIndex]);

        scheduler.Complete(request, 2, success: true);
        Assert.True(scheduler.TryCopyLifecycleStates(0, states));
        Assert.Equal(LumOnWorldProbeLifecycleState.Valid, states[request.StorageLinearIndex]);
    }
    #endregion
}
