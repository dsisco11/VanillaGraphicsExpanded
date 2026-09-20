using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises visibility rejection, published directions, and lighting preservation at the trace entry point.</summary>
public partial class LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests
{
    #region Visibility Controls
    /// <summary>Occluded or unavailable directional data cannot become the configured bright green sky fallback.</summary>
    [Theory]
    [InlineData(0.1f)]
    [InlineData(0f)]
    [InlineData(-0.1f)]
    public void WorldVisibility_RejectedOrUninitializedNeighbors_DoNotBecomeSky(float signedDistance)
    {
        var atlas = CreateVisibilityAtlas(signedDistance, signedDistance, 1f, 1f);
        RunWorldProbeTraceScenario(atlas, spacing: 2, anchorOffsetX: 0.25f,
            expectedRadiance: 0, minimumConfidence: 0, runPipeline: false);
    }

    /// <summary>Removing blocked bright neighbors must preserve the full radiance of visible neighbors.</summary>
    [Theory]
    [InlineData(false, 0.25f)]
    [InlineData(true, 0.25f)]
    [InlineData(false, 0.99f)]
    public void WorldVisibility_RenormalizesVisibleNeighbors_UsingLocalRatherThanStoragePositions(bool ringShift, float offset)
    {
        var atlas = ringShift ? CreateVisibilityAtlas(0.1f, 100f, 7f, 1f) : CreateVisibilityAtlas(100f, 0.1f, 1f, 7f);
        RunWorldProbeTraceScenario(atlas, spacing: 2, anchorOffsetX: offset,
            expectedRadiance: 1, ringShift: ringShift);
    }

    /// <summary>Unobstructed interpolation remains fully lit between probe centers.</summary>
    [Fact]
    public void WorldVisibility_UnobstructedNeighbors_RetainLighting()
    {
        var atlas = CreateVisibilityAtlas(100f, 100f, 1f, 1f);
        RunWorldProbeTraceScenario(atlas, spacing: 2, anchorOffsetX: 0.75f, expectedRadiance: 1);
    }

    /// <summary>A doorway in actual voxel geometry permits exterior contributions at an interior point.</summary>
    [Fact]
    public void WorldVisibility_OpenDoorway_PermitsExteriorContributions()
    {
        var world = WorldProbeRoomScenario.Create(exteriorBlockLight: 1);
        world.SetBlock(1, 0, 0, null);
        var atlas = new WorldProbeAtlasData(2, WorldProbeRoomScenario.TileSize);
        for (int z = 0; z < 2; z++)
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            atlas.SetProbe(WorldProbeRoomScenario.Trace(world, new Vector3d(0.5 + 2 * x, 0.5 + 2 * y, 0.5 + 2 * z), x, y, z));
        // Radiance varies by direction through the doorway; require a positive sample,
        // bounded by the supplied exterior light, while retaining the real trace metadata.
        RunWorldProbeTraceScenario(atlas, spacing: 2, anchorOffsetX: 0.25f,
            expectedRadiance: -2, runPipeline: false);
    }
    /// <summary>Unavailable coarse data cannot erase visible fine lighting or turn fine occlusion into sky.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorldVisibility_EmptyCoarseLevel_PreservesFineResult(bool blocked)
    {
        var atlas = CreateVisibilityAtlas(blocked ? 0.1f : 100f, blocked ? 0.1f : 100f, 1, 1);
        RunWorldProbeTraceScenario(atlas, spacing: 2, anchorOffsetX: 0.25f,
            expectedRadiance: blocked ? 0 : 1, minimumConfidence: blocked ? 0 : 0.2f,
            runPipeline: !blocked, emptyCoarseLevel: true);
    }
    #endregion

    #region Controlled Atlas
    /// <summary>Builds uniform directional tiles with independently controlled distances on the two X sides.</summary>
    private static WorldProbeAtlasData CreateVisibilityAtlas(float leftDistance, float rightDistance, float leftLight, float rightLight)
    {
        var atlas = new WorldProbeAtlasData(2, WorldProbeRoomScenario.TileSize);
        for (int y = 0; y < atlas.Height; y++)
        for (int x = 0; x < atlas.Width; x++)
        {
            bool right = (x / atlas.TileSize) % 2 != 0;
            float distance = right ? rightDistance : leftDistance;
            float light = right ? rightLight : leftLight;
            int offset = (y * atlas.Width + x) * 4;
            atlas.Radiance[offset] = atlas.Radiance[offset + 1] = atlas.Radiance[offset + 2] = light;
            atlas.Radiance[offset + 3] = Math.Sign(distance) * MathF.Log(1 + Math.Abs(distance));
        }
        for (int i = 0; i < atlas.Metadata.Length; i += 2) atlas.Metadata[i] = 1;
        return atlas;
    }
    #endregion
}
