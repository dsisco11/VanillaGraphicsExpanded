using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

public sealed class TraceSceneRegionCellPriorityTests
{
    [Fact]
    public void Priority_PrefersNearerRegions_WhenOtherTermsEqual()
    {
        var near = new TraceSceneRegionCell(new VectorInt3(0, 0, 0));
        var far = new TraceSceneRegionCell(new VectorInt3(10, 0, 0));

        var ctx = CreateContext(nowTick: 100, cameraBlockPos: new VectorInt3(0, 0, 0));

        float pn = near.CalculatePriority(in ctx);
        float pf = far.CalculatePriority(in ctx);

        Assert.True(pn > pf);
        Assert.True((near.LastPriorityReasons & TraceSceneRegionPriorityReason.Distance) != 0);
    }

    [Fact]
    public void Priority_PrefersStaleRegions_WhenDistanceEqual()
    {
        var stale = new TraceSceneRegionCell(new VectorInt3(1, 0, 0))
        {
            CurrentVersion = 2,
            AppliedVersion = 1,
        };

        var fresh = new TraceSceneRegionCell(new VectorInt3(1, 0, 0))
        {
            CurrentVersion = 2,
            AppliedVersion = 2,
        };

        var ctx = CreateContext(nowTick: 100, cameraBlockPos: new VectorInt3(32, 0, 0));

        float ps = stale.CalculatePriority(in ctx);
        float pf = fresh.CalculatePriority(in ctx);

        Assert.True(ps > pf);
        Assert.True((stale.LastPriorityReasons & TraceSceneRegionPriorityReason.Stale) != 0);
    }

    [Fact]
    public void Priority_SeenLoadedRecently_AddsBonus()
    {
        var loaded = new TraceSceneRegionCell(new VectorInt3(0, 0, 0))
        {
            LastSeenLoadedTick = 95,
        };

        var unloaded = new TraceSceneRegionCell(new VectorInt3(0, 0, 0))
        {
            LastSeenLoadedTick = 0,
        };

        var ctx = CreateContext(nowTick: 100, cameraBlockPos: new VectorInt3(0, 0, 0));

        float pl = loaded.CalculatePriority(in ctx);
        float pu = unloaded.CalculatePriority(in ctx);

        Assert.True(pl > pu);
        Assert.True((loaded.LastPriorityReasons & TraceSceneRegionPriorityReason.SeenLoadedRecently) != 0);
    }

    [Fact]
    public void Priority_CooldownSuppressed_IsNegativeInfinity()
    {
        var cell = new TraceSceneRegionCell(new VectorInt3(0, 0, 0))
        {
            NextEligibleTick = 200,
        };

        var ctx = CreateContext(nowTick: 100, cameraBlockPos: new VectorInt3(0, 0, 0));

        float p = cell.CalculatePriority(in ctx);

        Assert.True(float.IsNegativeInfinity(p));
        Assert.True((cell.LastPriorityReasons & TraceSceneRegionPriorityReason.CooldownSuppressed) != 0);
    }

    private static WorldCellPriorityContext CreateContext(long nowTick, VectorInt3 cameraBlockPos)
        => new(
            CameraBlockPos: cameraBlockPos,
            AnchorBlockPos: default,
            HasAnchor: false,
            WindowMinRegion: new VectorInt3(-1000, -1000, -1000),
            WindowMaxRegion: new VectorInt3(1000, 1000, 1000),
            HasWindow: true,
            NowTick: nowTick,
            IsCellLikelyLoaded: null);
}
