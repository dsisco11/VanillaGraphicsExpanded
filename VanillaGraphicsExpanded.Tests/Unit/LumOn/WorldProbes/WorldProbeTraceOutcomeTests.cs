using System.Numerics;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Separates proven sky visibility from finite traversal and unavailable world data.</summary>
public sealed class WorldProbeTraceOutcomeTests
{
    #region Actual geometry traversal
    /// <summary>Axis rays remain finite when unary negation introduces signed zero in their inactive components.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    [InlineData(1, -1)]
    [InlineData(2, 1)]
    [InlineData(2, -1)]
    public void Trace_SignedZeroAxisDirections_ReachRoomWalls(int axis, int sign)
    {
        var world = new ControlledVoxelWorld();
        world.AddRoom((-2, -2, -2), (2, 2, 2));
        Vector3 unit = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
        var direction = sign > 0 ? unit : -unit;
        var scene = new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world), false);
        Assert.Equal(WorldProbeTraceOutcome.Hit, scene.Trace(new(.5, .5, .5), direction, 8, CancellationToken.None, out var hit));
        Assert.Equal(1.5, hit.HitDistance);
    }

    /// <summary>A clear loaded route must reach the known world top before it can certify sky.</summary>
    [Theory]
    [InlineData(4, 8d, 512, (int)WorldProbeTraceOutcome.Sky)]
    [InlineData(4, 3.5d, 4, (int)WorldProbeTraceOutcome.Sky)]
    [InlineData(4, 3.49d, 4, (int)WorldProbeTraceOutcome.DistanceLimit)]
    [InlineData(4, 2d, 512, (int)WorldProbeTraceOutcome.DistanceLimit)]
    [InlineData(4, 8d, 2, (int)WorldProbeTraceOutcome.BudgetExhausted)]
    [InlineData(0, 8d, 512, (int)WorldProbeTraceOutcome.DistanceLimit)]
    [InlineData(1024, 1024d, 512, (int)WorldProbeTraceOutcome.BudgetExhausted)]
    public void Trace_ClearVerticalRoute_RequiresKnownWorldExit(int height, double distance, int steps, int expectedValue)
    {
        var world = new ControlledVoxelWorld { MapSizeY = height };
        var scene = new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world), false, steps);
        Assert.Equal((WorldProbeTraceOutcome)expectedValue, scene.Trace(new(.5, .5, .5), Vector3.UnitY, distance, CancellationToken.None, out var hit));
        Assert.Equal(default, hit);
        Assert.Empty(world.LightQueries);
    }

    /// <summary>Obstructions and streaming holes take precedence over a sky boundary farther along the ray.</summary>
    [Theory]
    [InlineData(false, (int)WorldProbeTraceOutcome.Hit)]
    [InlineData(true, (int)WorldProbeTraceOutcome.Unavailable)]
    public void Trace_RouteTowardSky_RespectsGeometryAndAvailability(bool unavailable, int expectedValue)
    {
        var world = new ControlledVoxelWorld { MapSizeY = 4, IsLoaded = p => !unavailable || p.Y != 2 };
        world.SetBlock(0, 2, 0, new Block { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes });
        var scene = new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world), false);
        Assert.Equal((WorldProbeTraceOutcome)expectedValue, scene.Trace(new(.5, .5, .5), Vector3.UnitY, 8, CancellationToken.None, out _));
    }

    /// <summary>Only outward upper-boundary traversal establishes sky; bottom and inward outside origins are unavailable.</summary>
    [Theory]
    [InlineData(4d, 1f, (int)WorldProbeTraceOutcome.Sky)]
    [InlineData(4d, -1f, (int)WorldProbeTraceOutcome.Unavailable)]
    [InlineData(-.5d, 1f, (int)WorldProbeTraceOutcome.Unavailable)]
    [InlineData(.5d, -1f, (int)WorldProbeTraceOutcome.Unavailable)]
    public void Trace_VerticalBoundary_HasExplicitMeaning(double y, float dy, int expectedValue)
    {
        var world = new ControlledVoxelWorld { MapSizeY = 4 };
        var scene = new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world), false);
        Assert.Equal((WorldProbeTraceOutcome)expectedValue, scene.Trace(new(.5, y, .5), new(0, dy, 0), 8, CancellationToken.None, out _));
    }

    /// <summary>Malformed input cannot be reinterpreted as a clear ray.</summary>
    [Theory]
    [InlineData(double.NaN, 1f, 8d)]
    [InlineData(double.PositiveInfinity, 1f, 8d)]
    [InlineData(.5d, float.NaN, 8d)]
    [InlineData(.5d, float.PositiveInfinity, 8d)]
    [InlineData(.5d, 0f, 8d)]
    [InlineData(.5d, 1f, double.PositiveInfinity)]
    [InlineData(.5d, 1f, double.NaN)]
    [InlineData(.5d, 1f, 0d)]
    public void Trace_InvalidInput_RejectsWithoutLighting(double x, float dy, double distance)
    {
        var world = new ControlledVoxelWorld { MapSizeY = 4 };
        var scene = new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world), false);
        Assert.Equal(WorldProbeTraceOutcome.Invalid, scene.Trace(new(x, .5, .5), new(0, dy, 0), distance, CancellationToken.None, out _));
        Assert.Empty(world.LightQueries);
    }
    #endregion

    #region Integration and retry
    /// <summary>The runtime fixture's old four-block reach does not cover the diagonal of its six-block-wide room.</summary>
    [Fact]
    public void Integrator_EnclosedRoom_RequiresEnoughRangeToReachEverySurface()
    {
        var world = new ControlledVoxelWorld { MapSizeY = 256 };
        world.AddRoom((0, 32, 0), (7, 39, 7));
        var scene = new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world), false);
        var work = Work() with { ProbePosWorld = new(4, 36, 4), MaxTraceDistanceWorld = 4, WorldProbeOctahedralTileSize = 8, WorldProbeAtlasTexelsPerUpdate = 64 };
        var integrator = new LumOnWorldProbeTraceIntegrator();
        var shortResult = integrator.TraceProbe(scene, work, CancellationToken.None);
        Assert.False(shortResult.Success);
        Assert.Equal(WorldProbeTraceFailureReason.DistanceLimit, shortResult.FailureReason);
        Assert.Empty(shortResult.AtlasSamples);
        var complete = integrator.TraceProbe(scene, work with { MaxTraceDistanceWorld = 16 }, CancellationToken.None);
        Assert.True(complete.Success);
        Assert.All(complete.AtlasSamples, sample => Assert.True(sample.AlphaEncodedDistSigned > 0));
    }

    /// <summary>Incomplete primary rays reject the batch rather than publishing negative sky alpha.</summary>
    [Theory]
    [InlineData((int)WorldProbeTraceOutcome.DistanceLimit, (int)WorldProbeTraceFailureReason.DistanceLimit)]
    [InlineData((int)WorldProbeTraceOutcome.BudgetExhausted, (int)WorldProbeTraceFailureReason.BudgetExhausted)]
    [InlineData((int)WorldProbeTraceOutcome.Unavailable, (int)WorldProbeTraceFailureReason.Aborted)]
    [InlineData((int)WorldProbeTraceOutcome.Invalid, (int)WorldProbeTraceFailureReason.Invalid)]
    public void Integrator_IncompletePrimary_RetainsFailureReason(int outcomeValue, int failureValue)
    {
        var result = new LumOnWorldProbeTraceIntegrator().TraceProbe(new OutcomeScene((WorldProbeTraceOutcome)outcomeValue), Work(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal((WorldProbeTraceFailureReason)failureValue, result.FailureReason);
        Assert.Empty(result.AtlasSamples);
        Assert.Equal(0, result.Confidence);
        Assert.Equal(37, result.SurfaceRevision);
    }

    /// <summary>A bounded clear cardinal proximity segment does not invalidate proven primary sky samples.</summary>
    [Fact]
    public void Integrator_ClearNearbySegment_IsNotAnUnresolvedLightingRay()
    {
        var scene = new OutcomeScene(WorldProbeTraceOutcome.Sky, nearbyDistance: 2);
        var result = new LumOnWorldProbeTraceIntegrator().TraceProbe(scene, Work() with { NearbySolidHitDistance = 2 }, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(1, scene.NearbyCalls);
        Assert.Equal(LumOnWorldProbeImportanceFlags.None, result.ImportanceFlags);
        Assert.All(result.AtlasSamples, sample => Assert.True(sample.AlphaEncodedDistSigned < 0));
    }

    /// <summary>A sky direction collected before an unresolved direction cannot leak through a failed batch.</summary>
    [Fact]
    public void Integrator_SkyThenDistanceLimit_DiscardsIncompleteBatch()
    {
        var result = new LumOnWorldProbeTraceIntegrator().TraceProbe(new SkyThenLimitScene(), Work(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(WorldProbeTraceFailureReason.DistanceLimit, result.FailureReason);
        Assert.Empty(result.AtlasSamples);
    }

    /// <summary>Legacy bounce lighting requires proven sky on secondary rays, not merely finite clear segments.</summary>
    [Theory]
    [InlineData((int)WorldProbeTraceOutcome.Sky, true)]
    [InlineData((int)WorldProbeTraceOutcome.DistanceLimit, false)]
    [InlineData((int)WorldProbeTraceOutcome.BudgetExhausted, false)]
    [InlineData((int)WorldProbeTraceOutcome.Unavailable, false)]
    public void Integrator_LegacySecondarySky_RequiresEstablishedVisibility(int outcome, bool lit)
    {
        var work = Work() with { DeferSurfaceLighting = false };
        var result = new LumOnWorldProbeTraceIntegrator().TraceProbe(new SecondaryOutcomeScene(work.ProbePosWorld, (WorldProbeTraceOutcome)outcome), work, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(lit, result.AtlasSamples.Any(sample => sample.RadianceRgb.LengthSquared() > 0));
    }

    /// <summary>A failed trace can be completed unsuccessfully and claimed again once actual sky becomes available.</summary>
    [Fact]
    public void Scheduler_UnavailableResult_CanRetryAndPublishEstablishedSky()
    {
        var scheduler = new LumOnWorldProbeScheduler(1, 1);
        var camera = new Vintagestory.API.MathTools.Vec3d();
        scheduler.UpdateOrigins(camera, 1);
        var request = scheduler.BuildUpdateList(0, camera, 1, [1], 1, 4096, 4).Single();
        Assert.True(scheduler.TryClaim(request, 0));
        var integrator = new LumOnWorldProbeTraceIntegrator();
        var failed = integrator.TraceProbe(new OutcomeScene(WorldProbeTraceOutcome.Unavailable), Work() with { Request = request }, CancellationToken.None);
        Assert.False(failed.Success);
        scheduler.Complete(request, 0, failed.Success);
        var retry = scheduler.BuildUpdateList(100, camera, 1, [1], 1, 4096, 4).Single();
        Assert.True(scheduler.TryClaim(retry, 100));
        var succeeded = integrator.TraceProbe(new OutcomeScene(WorldProbeTraceOutcome.Sky), Work() with { Request = retry }, CancellationToken.None);
        Assert.True(succeeded.Success);
        Assert.All(succeeded.AtlasSamples, sample => Assert.True(sample.AlphaEncodedDistSigned < 0));
        scheduler.Complete(retry, 100, succeeded.Success);
    }

    /// <summary>Creates a small deferred-lighting batch without secondary legacy lighting rays.</summary>
    private static LumOnWorldProbeTraceWorkItem Work() => new(0, new(0, new(), new(), 0), new(.5, .5, .5), 8, 4, 16, false, .25f, -1, 1e-6f, DeferSurfaceLighting: true, SurfaceRevision: 37);

    /// <summary>Isolates primary outcome integration from optional bounded cardinal proximity checks.</summary>
    private sealed class OutcomeScene(WorldProbeTraceOutcome outcome, double nearbyDistance = 0) : IWorldProbeTraceScene
    {
        public int NearbyCalls { get; private set; }

        /// <summary>Returns a controlled primary outcome or a clear finite proximity segment.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;
            if (nearbyDistance > 0 && maxDistance == nearbyDistance)
            {
                NearbyCalls++;
                return WorldProbeTraceOutcome.DistanceLimit;
            }
            return outcome;
        }
    }

    /// <summary>Exposes loss of batch atomicity after one successful sky direction.</summary>
    private sealed class SkyThenLimitScene : IWorldProbeTraceScene
    {
        private int calls;

        /// <summary>Reports sky once, then an unresolved finite trace.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;
            return calls++ == 0 ? WorldProbeTraceOutcome.Sky : WorldProbeTraceOutcome.DistanceLimit;
        }
    }

    /// <summary>Separates primary lit-wall hits from secondary sky visibility outcomes.</summary>
    private sealed class SecondaryOutcomeScene(Vector3d primaryOrigin, WorldProbeTraceOutcome secondaryOutcome) : IWorldProbeTraceScene
    {
        /// <summary>Primary rays hit an upward-facing surface with vanilla sunlight; all secondary rays use the requested outcome.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;
            if (!originWorld.Equals(primaryOrigin)) return secondaryOutcome;
            hit = new(1, 1, ProbeHitFace.Up, new(0, 0, 0), new(0, 1, 0), new(0, 1, 0), new(0, 0, 0, 1));
            return WorldProbeTraceOutcome.Hit;
        }
    }
    #endregion
}
