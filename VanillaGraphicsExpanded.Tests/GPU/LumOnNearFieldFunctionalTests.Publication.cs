using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Tests scene changes and the boundary between near-field geometry and distant lighting.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Publication Controls
    /// <summary>Dirty versions invalidate published lighting before a new region snapshot becomes ready.</summary>
    [Fact]
    public void DirtyGeneration_InvalidatesLightingUntilRepublished()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        var world = new ControlledVoxelWorld();
        fixture.Publish(world);
        Assert.True(TraceRed(Trace(fixture)) > 9);
        long revision = fixture.Scene.Revision;
        fixture.InvalidateAll();
        Assert.True(fixture.Scene.Revision > revision);
        AssertUnresolved(Trace(fixture));
        fixture.Publish(world);
        Assert.True(TraceRed(Trace(fixture)) > 9);
    }

    /// <summary>Aliased physical slots must not expose data from a previous world-region identity.</summary>
    [Fact]
    public void MovedWindow_RejectsOldSlotIdentities()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        fixture.MoveCenter(new VectorInt3(0, 0, 64));
        AssertUnresolved(Trace(fixture, worldOffset: new VectorInt3(0, 0, 64)));
    }

    /// <summary>An old async completion cannot overwrite a current ready region after a generation change.</summary>
    [Fact]
    public void StaleArtifact_CannotReplaceCurrentRegion()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        var replay = fixture.CaptureCompletionReplay();
        fixture.InvalidateAll();
        fixture.Publish(new ControlledVoxelWorld());
        long revision = fixture.Scene.Revision;
        replay();
        fixture.Pump();
        Assert.Equal(revision, fixture.Scene.Revision);
        var result = Trace(fixture);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsPublication_StaleArtifact_CannotReplaceCurrentRegion).AssertEqual();
    }

    /// <summary>Requires darkness and unavailable confidence, not merely a zero-valued RGB buffer.</summary>
    private void AssertUnresolved(TraceImages result)
    {
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsLighting_RawEmission_RemainsUnavailableWithoutSurfaceCache).AssertEqual();
    }
    #endregion
}
