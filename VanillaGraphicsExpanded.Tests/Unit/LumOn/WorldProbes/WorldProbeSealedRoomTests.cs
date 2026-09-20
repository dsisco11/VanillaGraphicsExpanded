using System.Numerics;
using System.Threading;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Separates sealed-room tracing and surface-light lookup from GPU interpolation.</summary>
public sealed class WorldProbeSealedRoomTests
{
    #region Sealed Room
    /// <summary>Every interior direction must hit a wall and remain dark even with bright exterior cells.</summary>
    [Theory]
    [InlineData(0, 0, 0.5, 0.5, 0.5)]
    [InlineData(1, 1, 0.5, 0.5, 0.5)]
    [InlineData(1, 1, -2.5, -2.5, -2.5)]
    [InlineData(1, 1, 0.75, 2.5, 2.5)]
    public void SealedRoom_AllDirectionsHitAndRemainDark(float blockLight, float sunlight, double x, double y, double z)
    {
        var world = WorldProbeRoomScenario.Create(blockLight, sunlight);
        var result = WorldProbeRoomScenario.Trace(world, new Vector3d(x, y, z));
        Assert.True(result.Success);
        Assert.Equal(256, result.AtlasSamples.Length);
        Assert.Equal(256, result.AtlasSamples.Select(s => (s.OctX, s.OctY)).Distinct().Count());
        Assert.True(result.Confidence > 0);
        Assert.All(result.AtlasSamples, sample =>
        {
            Assert.True(sample.AlphaEncodedDistSigned > 0, "A miss or absent direction must not count as a dark hit.");
            Assert.InRange(sample.RadianceRgb.Length(), 0, 1e-6f);
        });
        Assert.NotEmpty(world.LightQueries);
        Assert.All(world.LightQueries, p =>
        {
            Assert.InRange(p.X, -3, 0);
            Assert.InRange(p.Y, -3, 3);
            Assert.InRange(p.Z, -3, 3);
        });
    }

    /// <summary>Bright exterior probes prove that the controlled light source is actually consumed.</summary>
    [Fact]
    public void ExteriorProbe_ReceivesControlledBlockLight()
    {
        var world = WorldProbeRoomScenario.Create(exteriorBlockLight: 1);
        var result = WorldProbeRoomScenario.Trace(world, new Vector3d(2.5, 0.5, 0.5));
        Assert.True(result.Success);
        Assert.Equal(256, result.AtlasSamples.Length);
        Assert.All(result.AtlasSamples, sample =>
        {
            Assert.True(sample.AlphaEncodedDistSigned > 0);
            Assert.InRange(sample.RadianceRgb.X, 0.999f, 1.001f);
            Assert.InRange(sample.RadianceRgb.Y, 0.999f, 1.001f);
            Assert.InRange(sample.RadianceRgb.Z, 0.999f, 1.001f);
        });
    }
    #endregion

    #region Fixture Controls
    /// <summary>Opening a wall exposes exterior light and closing it restores a dark fresh trace.</summary>
    [Fact]
    public void DoorwayMutation_ChangesFreshTraceWithoutCannedRayResults()
    {
        var world = WorldProbeRoomScenario.Create(exteriorBlockLight: 1);
        world.SetBlock(1, 0, 0, null);
        var scene = world.CreateTraceScene();
        Assert.Equal(WorldProbeTraceOutcome.Hit, scene.Trace(new Vector3d(0.5, 0.5, 0.5), Vector3.UnitX, 64, CancellationToken.None, out var hit));
        Assert.True(hit.HitDistance > 7);
        Assert.Equal(1f, hit.SampleLightRgbS.X);
        var open = WorldProbeRoomScenario.Trace(world, new Vector3d(0.5, 0.5, 0.5));
        Assert.True(open.Success);
        Assert.Contains(open.AtlasSamples, sample => sample.RadianceRgb.X > 0.9f);
        world.AddRoom((-4, -4, -4), (1, 4, 4));
        var closed = WorldProbeRoomScenario.Trace(world, new Vector3d(0.5, 0.5, 0.5));
        Assert.True(closed.Success);
        Assert.All(closed.AtlasSamples, sample => Assert.InRange(sample.RadianceRgb.Length(), 0, 1e-6f));
    }

    /// <summary>Unavailable cells must abort instead of becoming successful black samples.</summary>
    [Fact]
    public void UnloadedWorld_DoesNotPassTheDarknessBaseline()
    {
        var world = WorldProbeRoomScenario.Create();
        world.IsLoaded = _ => false;
        Assert.False(WorldProbeRoomScenario.Trace(world, new Vector3d(0.5, 0.5, 0.5)).Success);
    }
    #endregion
}
