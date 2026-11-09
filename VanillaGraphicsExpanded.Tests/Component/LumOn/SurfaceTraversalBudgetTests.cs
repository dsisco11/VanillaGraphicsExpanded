using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises long production Surface Cache rays without treating incomplete traversal as sky.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceTraversalBudgetTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Public API
    #region Long routes
    /// <summary>A diagonal reaching the known world top crosses more than 512 cells and preserves history on finite limits.</summary>
    [Fact]
    public void ExpandedBudgetReachesSkyBeyondOldLoopCeiling()
    {
        // Arrange: Seed history at a distant floor and choose a diagonal route to the known world top.
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:240,surfaceResolution:256);
        floor.Geometry.Publish(900);floor.Seed();floor.SetIndirect(8,4);
        // Place the original captured floor near the corner of the expanded domain, retaining its surface identity.
        floor.Geometry.Move(TraceGeometryCoverage.Plan(new(128,128,128),true,256,240));
        floor.Geometry.Publish(900);
        uint frame=FrameForDirection(diagonal:true);
        var before=floor.Read(floor.Snapshot.IndirectIrradiance);
        // Act: First stop at the finite step ceiling.
        Assert.False(floor.BounceSample(rays:1,steps:512,frame:frame));
        // Assert: The step-limited route preserves history and records budget exhaustion.
        Assert.NotEqual(0u,floor.LastWorkFlags&0x20000000u);
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
        GpuTestFence.WaitForGpuOrSkip("Long traversal budget counter");floor.Diagnostics.Poll();
        var budget=floor.Diagnostics.Snapshot(SurfaceWorkStage.Indirect).Counters;
        Assert.Equal(1ul,budget[(int)SurfaceWorkCounter.Budget]);Assert.Equal(0ul,budget[(int)SurfaceWorkCounter.Distance]);
        // Act: Raise the step ceiling but cap physical travel distance for the same route.
        Assert.False(floor.BounceSample(rays:1,steps:1024,frame:frame,maxTraceDistance:64));
        Assert.NotEqual(0u,floor.LastWorkFlags&0x20000000u);
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
        GpuTestFence.WaitForGpuOrSkip("Finite traversal distance counter");floor.Diagnostics.Poll();
        var distance=floor.Diagnostics.Snapshot(SurfaceWorkStage.Indirect).Counters;
        Assert.Equal(1ul,distance[(int)SurfaceWorkCounter.Budget]);Assert.Equal(1ul,distance[(int)SurfaceWorkCounter.Distance]);
        // Act: Remove both limits so the route can reach the known world boundary.
        Assert.True(floor.BounceSample(rays:1,steps:1024,frame:frame));
        // Assert: Confirmed sky resolves with the expected irradiance and complete sample weight.
        var resolved=floor.Read(floor.Snapshot.IndirectIrradiance);
        Assert.InRange(resolved[0],6.39f,6.41f);Assert.Equal(4,resolved[3]);
    }

    /// <summary>The ordinary 64-step limit cannot certify a distant top boundary, while the configured default can.</summary>
    [Fact]
    public void LongUpwardRoutePreservesHistoryUntilKnownSky()
    {
        // Arrange: Seed a floor below a known world top and select an upward ray.
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:120,surfaceResolution:256);
        floor.Geometry.Publish(900);floor.Seed();floor.SetIndirect(8,4);
        uint frame=FrameForDirection(diagonal:false);
        var before=floor.Read(floor.Snapshot.IndirectIrradiance);
        // Act: Trace with the ordinary limit first.
        Assert.False(floor.BounceSample(rays:1,steps:64,frame:frame));
        // Assert: The incomplete traversal preserves prior history.
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
        // Act: Raise the step budget for the unchanged upward route.
        Assert.True(floor.BounceSample(rays:1,steps:1024,frame:frame));
        // Assert: The route contributes only after it reaches confirmed world sky.
        Assert.InRange(floor.Read(floor.Snapshot.IndirectIrradiance)[0],6.39f,6.41f);
    }
    #endregion
    #endregion

    #region Private
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
