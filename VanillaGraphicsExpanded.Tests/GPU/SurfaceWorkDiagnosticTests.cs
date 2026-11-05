using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises asynchronous diagnostic ownership and producer outcomes without changing lighting policy.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceWorkDiagnosticTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Numerical transparency
    /// <summary>Instrumentation preserves seeded, completed and unresolved lighting results exactly.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DiagnosticsDoNotChangeLighting(bool unresolved)
    {
        EnsureContextValid();
        var observed=new List<float[]>();
        foreach(bool enabled in new[]{false,true})
        {
            using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:35);
            room.Diagnostics.Enabled=enabled;room.Seed();
            Assert.Equal(!unresolved,room.BounceSample(steps:unresolved?0u:256u));
            observed.Add(room.Read(room.Snapshot.DirectIrradiance)
                .Concat(room.Read(room.Snapshot.IndirectIrradiance)).Concat(room.Read(room.Snapshot.OutgoingRadiance)).ToArray());
        }
        Assert.Equal(observed[0],observed[1]);
    }
    #endregion

    #region Outcome counters
    /// <summary>Seed reuse is unchanged work, while direct, combine and reset writes remain separate completed stages.</summary>
    [Fact]
    public void StagesSeparateUnchangedSeedFromCompletedWrites()
    {
        EnsureContextValid();using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);room.Seed();
        SurfaceWorkDiagnosticsTestWait.Drain(room.Diagnostics,"Stage setup counters");
        (uint Operation,SurfaceWorkStage Stage)[] stages=[(0u,SurfaceWorkStage.Seed),(4u,SurfaceWorkStage.Direct),(2u,SurfaceWorkStage.Combine),(3u,SurfaceWorkStage.Reset)];
        foreach(var entry in stages)
        {
            var before=room.Diagnostics.Snapshot(entry.Stage);
            room.DispatchDiagnosticWork(entry.Operation);
            SurfaceWorkDiagnosticsTestWait.Drain(room.Diagnostics,"Separated surface stage");
            var after=room.Diagnostics.Snapshot(entry.Stage);
            Assert.Equal(1,after.Collected-before.Collected);Assert.Equal(4,after.Pages-before.Pages);
            Assert.Equal(256ul,after.Counters[(int)SurfaceWorkCounter.Texels]-before.Counters[(int)SurfaceWorkCounter.Texels]);
            Assert.Equal(entry.Operation==0?0ul:256ul,after.Counters[(int)SurfaceWorkCounter.Completed]-before.Counters[(int)SurfaceWorkCounter.Completed]);
            if(entry.Operation==0)Assert.Equal(256ul,after.Counters[(int)SurfaceWorkCounter.Unchanged]-before.Counters[(int)SurfaceWorkCounter.Unchanged]);
        }
    }

    /// <summary>Single-texel dispatches report completion separately from ray attempts and their exact rejection class.</summary>
    [Theory]
    [InlineData("sky",(int)SurfaceWorkCounter.Sky,true)]
    [InlineData("budget",(int)SurfaceWorkCounter.Budget,false)]
    [InlineData("coverage",(int)SurfaceWorkCounter.Outside,false)]
    [InlineData("unsupported",(int)SurfaceWorkCounter.Unsupported,false)]
    [InlineData("origin-outside",(int)SurfaceWorkCounter.OriginOutside,false)]
    [InlineData("origin-unpublished",(int)SurfaceWorkCounter.OriginUnpublished,false)]
    [InlineData("unseeded",(int)SurfaceWorkCounter.Unseeded,false)]
    public void SingleTexelClassifiesItsActualOutcome(string scenario,int counter,bool completed)
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:35);
        if(scenario!="unseeded")floor.Seed();
        else floor.DispatchDiagnosticWork(3); // New GPU storage is undefined until explicitly reset.
        if(scenario=="origin-unpublished")floor.Geometry.Dirty();
        if(scenario is "coverage" or "origin-outside")
        {
            var domain=floor.Geometry.Plan.Surface!.Value;
            floor.Geometry.Move(floor.Geometry.Plan with {Surface=new(domain.Min,new(domain.Max.X,scenario=="coverage"?34:33,domain.Max.Z))});
        }
        if(scenario=="unsupported")
        {
            var sample=floor.Geometry.Sample;
            floor.Geometry.Sample=(x,y,z)=>y>=34?new(3,0,0):sample(x,y,z);
            floor.Geometry.Dirty();floor.Geometry.Publish();
        }
        Assert.Equal(completed,floor.BounceSample(rays:1,steps:scenario=="budget"?0u:256u));
        SurfaceWorkDiagnosticsTestWait.Drain(floor.Diagnostics,"Surface outcome counters");
        var measurement=floor.Diagnostics.Snapshot(SurfaceWorkStage.Indirect);
        Assert.Equal(1,measurement.Submitted);Assert.Equal(1,measurement.Collected);Assert.Equal(0,measurement.ReadFailures);
        Assert.Equal(1ul,measurement.Counters[(int)SurfaceWorkCounter.Texels]);
        Assert.Equal(completed?1ul:0ul,measurement.Counters[(int)SurfaceWorkCounter.Completed]);
        Assert.Equal(1ul,measurement.Counters[(int)counter]);
        Assert.Equal(scenario.StartsWith("origin-")||scenario=="unseeded"?0ul:1ul,measurement.Counters[(int)SurfaceWorkCounter.Rays]);
    }

    /// <summary>Lighting-unavailable hit rays remain geometry hits and never count as useful indirect completion.</summary>
    [Fact]
    public void MissingHitLightingSeparatesAttemptFromCompletion()
    {
        EnsureContextValid();using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);room.Seed();room.WithholdPages();
        Assert.False(room.BounceSample(rays:1));
        SurfaceWorkDiagnosticsTestWait.Drain(room.Diagnostics,"Missing hit lighting counters");
        var counters=room.Diagnostics.Snapshot(SurfaceWorkStage.Indirect).Counters;
        Assert.Equal(1ul,counters[(int)SurfaceWorkCounter.Rays]);Assert.Equal(1ul,counters[(int)SurfaceWorkCounter.Hit]);
        Assert.Equal(1ul,counters[(int)SurfaceWorkCounter.HitLighting]);Assert.Equal(0ul,counters[(int)SurfaceWorkCounter.Completed]);
    }

    /// <summary>Unavailable source geometry rejects capture explicitly instead of counting attempted texels as captured.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CaptureClassifiesOutsideAndUnpublishedSources(bool unpublished)
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:35);
        if(unpublished)floor.Geometry.Dirty();
        else
        {
            var domain=floor.Geometry.Plan.Surface!.Value;
            floor.Geometry.Move(floor.Geometry.Plan with {Surface=new(new(domain.Min.X,34,domain.Min.Z),domain.Max)});
        }
        var measurement=floor.Capture(diagnostics:true,expectComplete:false);
        Assert.Equal(1,measurement.Collected);Assert.Equal(4,measurement.Pages);
        Assert.Equal(256ul,measurement.Counters[(int)SurfaceWorkCounter.Texels]);
        Assert.Equal(0ul,measurement.Counters[(int)SurfaceWorkCounter.Completed]);
        Assert.Equal(256ul,measurement.Counters[(int)(unpublished?SurfaceWorkCounter.CaptureUnpublished:SurfaceWorkCounter.CaptureOutside)]);
    }

    /// <summary>Published solid geometry with no material is distinguished from unavailable geometry during capture.</summary>
    [Fact]
    public void CaptureReportsMissingMaterialOnPublishedSolid()
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(ImageAssertions, skyFloor:true,worldHeight:35);
        floor.Geometry.Sample=(_,_,_)=>new(2,0,0);floor.Geometry.Dirty();floor.Geometry.Publish();
        var measurement=floor.Capture(diagnostics:true,expectComplete:false);
        Assert.Equal(256ul,measurement.Counters[(int)SurfaceWorkCounter.CaptureMaterial]);
        Assert.Equal(0ul,measurement.Counters[(int)SurfaceWorkCounter.Completed]);
        Assert.Equal(0ul,measurement.Counters[(int)SurfaceWorkCounter.CaptureOutside]);
        Assert.Equal(0ul,measurement.Counters[(int)SurfaceWorkCounter.CaptureUnpublished]);
    }
    #endregion

    #region Bounded collection
    /// <summary>Repeated submissions keep at most eight pending samples and stable snapshots never poll the GPU.</summary>
    [Fact]
    public void CollectorIsBoundedAndSnapshotsAreIndependent()
    {
        // Arrange: exercise the collector through a small precompiled compute writer, without constructing a lighting enclosure.
        EnsureContextValid();using var diagnostics=new SurfaceWorkDiagnostics();using var assets=new BinaryShaderApiFixture();
        using var probe=SurfaceWorkCollectorComputeProgram.Create(assets.Api,diagnostics);
        var saved=diagnostics.Snapshot(SurfaceWorkStage.Indirect);
        for(int dispatch=0;dispatch<32;dispatch++)
        {
            // Act: each admitted slot receives a real GPU counter write before its timestamp and fence are closed.
            if(diagnostics.Begin(SurfaceWorkStage.Indirect,1))probe.Dispatch(1);
            diagnostics.End();
            Assert.InRange(diagnostics.PendingCount,0,8);
            int pending=diagnostics.PendingCount;
            _=diagnostics.Snapshot(SurfaceWorkStage.Indirect);
            Assert.Equal(pending,diagnostics.PendingCount);
        }
        Assert.Equal(32,diagnostics.Snapshot(SurfaceWorkStage.Indirect).Submitted);
        SurfaceWorkDiagnosticsTestWait.Drain(diagnostics,"Bounded diagnostics completion");
        var current=diagnostics.Snapshot(SurfaceWorkStage.Indirect);
        Assert.True(current.Collected>0,"At least one admitted diagnostic slot must complete timestamp and SSBO readback.");
        Assert.Equal(32,current.Submitted);Assert.Equal(current.Submitted,current.Collected+current.Skipped);
        Assert.Equal(0,diagnostics.PendingCount);Assert.Equal(0,current.ReadFailures);
        Assert.Equal(0,saved.Submitted);Assert.All(saved.Counters,count=>Assert.Equal(0ul,count));
        Assert.Equal((ulong)(current.Collected*64),current.Counters[(int)SurfaceWorkCounter.Texels]);
        Assert.Equal((ulong)current.Collected,current.Counters[(int)SurfaceWorkCounter.Completed]);
        // Assert: disabled samples skip collection, snapshots remain detached, and disposal rejects new work.
        diagnostics.Enabled=false;Assert.False(diagnostics.Begin(SurfaceWorkStage.Indirect,1));diagnostics.End();
        Assert.Equal(current.Collected,diagnostics.Snapshot(SurfaceWorkStage.Indirect).Collected);
        Assert.Equal(current.Skipped+1,diagnostics.Snapshot(SurfaceWorkStage.Indirect).Skipped);
        diagnostics.Dispose();Assert.Equal(0,diagnostics.PendingCount);
        Assert.Throws<ObjectDisposedException>(()=>diagnostics.Begin(SurfaceWorkStage.Indirect,1));
    }
    #endregion
}
