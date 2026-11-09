using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Tests scene changes and the boundary between near-field geometry and distant lighting.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Public API
    #region Publication Controls
    /// <summary>Dirty versions invalidate published lighting before a new region snapshot becomes ready.</summary>
    [Fact]
    public void DirtyGeneration_InvalidatesLightingUntilRepublished()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish a clear scene and confirm its first production trace admits distant cache lighting.
        using var fixture = new NearFieldVoxelFixture();
        var world = new ControlledVoxelWorld();
        fixture.Publish(world);
        Assert.True(TraceRed(Trace(fixture)) > 9);
        long revision = fixture.Scene.Revision;

        // Act and assert: Invalidate the published generation; lighting becomes unavailable until republishing.
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
        // Arrange: Publish the original clear ring and retain its physical slots.
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());

        // Act and assert: Move the window, then trace its new origin without accepting an aliased old slot.
        fixture.MoveCenter(new VectorInt3(0, 0, 64));
        AssertUnresolved(Trace(fixture, worldOffset: new VectorInt3(0, 0, 64)));
    }

    /// <summary>An old async completion cannot overwrite a current ready region after a generation change.</summary>
    [Fact]
    public void StaleArtifact_CannotReplaceCurrentRegion()
    {
        EnsureShaderTestAvailable();
        // Arrange: Capture a completion from the original generation before replacing it.
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        var replay = fixture.CaptureCompletionReplay();
        fixture.InvalidateAll();
        fixture.Publish(new ControlledVoxelWorld());
        long revision = fixture.Scene.Revision;

        // Act: Replay and pump the stale completion after the current generation is ready.
        replay();
        fixture.Pump();

        // Assert: The stale result cannot revise current data and the GPU trace still observes the current generation.
        Assert.Equal(revision, fixture.Scene.Revision);
        var result = Trace(fixture);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsPublication_StaleArtifact_CannotReplaceCurrentRegion).AssertEqual();
    }

    #endregion

    #endregion
    #region Private
    /// <summary>Requires darkness and unavailable confidence, not merely a zero-valued RGB buffer.</summary>
    private void AssertUnresolved(TraceImages result)
    {
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsLighting_RawEmission_RemainsUnavailableWithoutSurfaceCache).AssertEqual();
    }
    #endregion
}
