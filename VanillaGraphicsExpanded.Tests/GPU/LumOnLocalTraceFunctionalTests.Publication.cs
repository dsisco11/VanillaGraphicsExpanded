using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Tests scene changes and the boundary between local geometry and distant lighting.</summary>
public sealed partial class LumOnLocalTraceFunctionalTests
{
    #region Publication Controls
    /// <summary>Dirty versions invalidate published lighting before a new region snapshot becomes ready.</summary>
    [Fact]
    public void DirtyGeneration_InvalidatesLightingUntilRepublished()
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture();
        var world = new ControlledVoxelWorld();
        fixture.Publish(world);
        Assert.True(Trace(fixture).Radiance[0] > 9);
        long revision = fixture.Scene.Revision;
        fixture.Versions.BumpGlobalGeneration();
        fixture.Scene.Prepare(default, fixture.Versions);
        Assert.True(fixture.Scene.Revision > revision);
        AssertUnresolved(Trace(fixture));
        fixture.Publish(world);
        Assert.True(Trace(fixture).Radiance[0] > 9);
    }

    /// <summary>Aliased physical slots must not expose data from a previous world-region identity.</summary>
    [Fact]
    public void MovedWindow_RejectsOldSlotIdentities()
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        fixture.Scene.Prepare(new VectorInt3(0, 0, 64), fixture.Versions);
        AssertUnresolved(Trace(fixture, worldOffset: new VectorInt3(0, 0, 64)));
    }

    /// <summary>An old async completion cannot overwrite a current ready region after a generation change.</summary>
    [Fact]
    public void StaleArtifact_CannotReplaceCurrentRegion()
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Versions.BumpGlobalGeneration();
        fixture.Publish(new ControlledVoxelWorld());
        long revision = fixture.Scene.Revision;
        var key = ChunkKey.FromChunkCoords(-1, -1, -1);
        fixture.Scene.Publish(new VanillaGraphicsExpanded.LumOn.Scene.LumonSceneTraceSceneRegionArtifact(
            key, 0, new VectorInt3(-1, -1, -1), new uint[32768])
            { LocalCells = new VanillaGraphicsExpanded.LumOn.Scene.LocalTracing.LocalTraceSourceCell[32768] },
            new VanillaGraphicsExpanded.LumOn.Scene.LocalTracing.LocalTraceMaterialRegistry(), fixture.Versions);
        Assert.Equal(revision, fixture.Scene.Revision);
        var result = Trace(fixture);
        for (int i = 0; i < result.Radiance.Length; i += 4) Assert.InRange(result.Radiance[i], 9.99f, 10.01f);
    }

    /// <summary>Requires darkness and unavailable confidence, not merely a zero-valued RGB buffer.</summary>
    private static void AssertUnresolved((float[] Radiance, float[] Meta) result)
    {
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            for (int c = 0; c < 3; c++) Assert.Equal(0, result.Radiance[i + c]);
            Assert.Equal(0, result.Meta[i / 2]);
        }
    }
    #endregion
}
