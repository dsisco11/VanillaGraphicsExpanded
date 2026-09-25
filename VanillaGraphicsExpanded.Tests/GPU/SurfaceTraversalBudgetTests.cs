using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises long production Surface Cache rays without treating incomplete traversal as sky.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceTraversalBudgetTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Long routes
    /// <summary>A diagonal reaching the known world top crosses more than 512 cells and preserves history on finite limits.</summary>
    [Fact]
    public void ExpandedBudgetReachesSkyBeyondOldLoopCeiling()
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:240,surfaceResolution:256);
        floor.Geometry.Publish(900);floor.Seed();floor.SetIndirect(8,4);
        // Place the original captured floor near the corner of the expanded domain, retaining its surface identity.
        floor.Geometry.Move(TraceGeometryCoverage.Plan(new(128,128,128),true,256,240));
        floor.Geometry.Publish(900);
        uint frame=FrameForDirection(diagonal:true);
        var before=floor.Read(floor.Snapshot.IndirectIrradiance);
        Assert.False(floor.BounceSample(rays:1,steps:512,frame:frame));
        Assert.NotEqual(0u,floor.LastWorkFlags&0x20000000u);
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
        GpuTestFence.WaitForGpuOrSkip("Long traversal budget counter");floor.Diagnostics.Poll();
        var budget=floor.Diagnostics.Snapshot(SurfaceWorkStage.Indirect).Counters;
        Assert.Equal(1ul,budget[(int)SurfaceWorkCounter.Budget]);Assert.Equal(0ul,budget[(int)SurfaceWorkCounter.Distance]);
        Assert.False(floor.BounceSample(rays:1,steps:1024,frame:frame,maxTraceDistance:64));
        Assert.NotEqual(0u,floor.LastWorkFlags&0x20000000u);
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
        GpuTestFence.WaitForGpuOrSkip("Finite traversal distance counter");floor.Diagnostics.Poll();
        var distance=floor.Diagnostics.Snapshot(SurfaceWorkStage.Indirect).Counters;
        Assert.Equal(1ul,distance[(int)SurfaceWorkCounter.Budget]);Assert.Equal(1ul,distance[(int)SurfaceWorkCounter.Distance]);
        Assert.True(floor.BounceSample(rays:1,steps:1024,frame:frame));
        var resolved=floor.Read(floor.Snapshot.IndirectIrradiance);
        Assert.InRange(resolved[0],6.39f,6.41f);Assert.Equal(4,resolved[3]);
    }

    /// <summary>The ordinary 64-step limit cannot certify a distant top boundary, while the configured default can.</summary>
    [Fact]
    public void LongUpwardRoutePreservesHistoryUntilKnownSky()
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:120,surfaceResolution:256);
        floor.Geometry.Publish(900);floor.Seed();floor.SetIndirect(8,4);
        uint frame=FrameForDirection(diagonal:false);
        var before=floor.Read(floor.Snapshot.IndirectIrradiance);
        Assert.False(floor.BounceSample(rays:1,steps:64,frame:frame));
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
        Assert.True(floor.BounceSample(rays:1,steps:1024,frame:frame));
        Assert.InRange(floor.Read(floor.Snapshot.IndirectIrradiance)[0],6.39f,6.41f);
    }
    #endregion

    #region Deterministic direction selection
    /// <summary>Chooses an original producer seed with a route independently constrained to remain inside the authored domain.</summary>
    private static uint FrameForDirection(bool diagonal)
    {
        for(uint frame=0;frame<100000;frame++)
        {
            var request=new SurfaceFallbackRequest{Seed=Squirrel3Noise.HashU(1,27,frame),Normal=new(0,1,0,512)};
            var direction=SurfaceFallbackWorker.Direction(request,0);
            if(diagonal ? direction.X>.5f && direction.Z>.5f && direction.Y>.56f && direction.Y<.63f && direction.X/direction.Y<1.1f && direction.Z/direction.Y<1.1f : direction.Y>.99f)
                return frame;
        }
        throw new InvalidOperationException("No bounded deterministic test direction found.");
    }
    #endregion
}
