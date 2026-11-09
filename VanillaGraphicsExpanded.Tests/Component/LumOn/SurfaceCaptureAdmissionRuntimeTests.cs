using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises deferred capture admission through registered geometry and Surface Cache owners.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class SurfaceCaptureAdmissionRuntimeTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Public API
    #region Deferred source recovery
    /// <summary>A recovered backlog drains in eight-page retry batches despite repeated dirty demand during its sweep.</summary>
    [Fact]
    public void RecoveredBacklogMakesBoundedProgressWhileVisibilityChanges()
    {
        // Arrange: Fill 64 resident requests while their source material is unavailable.
        EnsureContextValid();using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, requestedPages:64);
        runtime.Config.LumOn.LumonScene.TraceScene.ClipmapResolution=64;
        bool materialReady=false;
        runtime.TransformVoxel=(x,y,z,voxel)=>!materialReady&&x==0?voxel with {Geometry=2u}:voxel;
        runtime.RunUntil(()=>CaptureStateCounts(runtime).Resident==64,160);
        Assert.Equal((0L,0L),CaptureCounts(runtime));
        // Act: Publish the relevant material, then keep changing visibility and dirty demand during recovery.
        materialReady=true;
        foreach(var source in runtime.Sources.Where(source=>!source.Disposed))source.MarkChunkDirty(ChunkKey.FromChunkCoords(0,1,0));
        long previous=0;int productiveFrames=0;
        for(int frame=0;frame<120&&!runtime.AllRequestedCaptured();frame++)
        {
            // Explicit repeated demand must not restart an already-admitted finite sweep.
            if(previous>0)runtime.Feedback.NotifyAllDirty("Repeated capture admission test demand");
            runtime.VisibleFeedbackPages=(frame&1)==0?1:64;runtime.Frame();
            long attempts=CaptureCounts(runtime).Attempts;
            Assert.InRange(attempts-previous,0,8);
            if(attempts>previous)productiveFrames++;
            previous=attempts;
        }
        // Assert: Recovery drains in bounded batches, completes all residents, and records no failures.
        Assert.True(runtime.AllRequestedCaptured());Assert.Equal(64,previous);
        Assert.InRange(productiveFrames,8,64);Assert.Equal(0,CaptureCounts(runtime).Failures);
    }

    /// <summary>Forty-eight out-of-domain residents cannot consume GPU attempts or starve sixteen covered patches.</summary>
    [Fact]
    public void DeferredResidentsDoNotStarveEligiblePages()
    {
        // Arrange: Request 64 pages while geometry covers only 16 of their locations.
        EnsureContextValid();using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, requestedPages:64);
        runtime.PrimeGeometry();
        // Act: Allow production capture admission to settle, then keep the same demand for another 24 frames.
        runtime.RunUntil(()=>CaptureStateCounts(runtime)==(64,16));
        // Assert: Out-of-domain residents do not consume GPU attempts or displace covered patches.
        var before=CaptureCounts(runtime);Assert.Equal(0,before.Failures);Assert.Equal(16,before.Attempts);
        for(int frame=0;frame<24;frame++)runtime.Frame();
        Assert.Equal((64,16),CaptureStateCounts(runtime));Assert.Equal(before,CaptureCounts(runtime));
    }

    /// <summary>Missing source material never reaches the GPU, including after an unrelated chunk publishes.</summary>
    [Fact]
    public void MissingMaterialWaitsForRelevantSourceChange()
    {
        // Arrange: Publish a solid voxel whose material remains unready and capture its deferred state.
        EnsureContextValid();using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions);
        bool materialReady=false;
        runtime.TransformVoxel=(x,y,z,voxel)=>!materialReady&&x==0?voxel with {Geometry=2u}:voxel;
        runtime.PrimeGeometry();
        for(int frame=0;frame<16;frame++)runtime.Frame();
        Assert.False(runtime.AllRequestedCaptured());Assert.Equal((0L,0L),CaptureCounts(runtime));
        AssertPendingResident(runtime);
        long checks=EligibilityChecks(runtime);
        long revision=runtime.Geometry.Resources!.Revision;
        foreach(var source in runtime.Sources.Where(source=>!source.Disposed))source.MarkChunkDirty(ChunkKey.FromChunkCoords(-1,1,0));
        runtime.RunUntil(()=>runtime.Geometry.Resources!.Revision>revision);
        for(int frame=0;frame<8;frame++)runtime.Frame();
        Assert.Equal((0L,0L),CaptureCounts(runtime));AssertPendingResident(runtime);
        Assert.Equal(checks,EligibilityChecks(runtime));
        // Act: Publish the material-bearing source revision after the unrelated revision was observed above.
        materialReady=true;
        foreach(var source in runtime.Sources.Where(source=>!source.Disposed))source.MarkChunkDirty(ChunkKey.FromChunkCoords(0,1,0));
        runtime.RunUntil(()=>runtime.AllRequestedCaptured()&&runtime.TryGetLighting(out _));
        // Assert: Only the relevant source change resumes capture, without charging failures while deferred.
        var capture=CaptureCounts(runtime);Assert.Equal(0,capture.Failures);Assert.True(capture.Attempts>0);
    }

    /// <summary>Unavailable geometry can leave resident requests pending without repeated GPU failures and later recover.</summary>
    [Fact]
    public void UnpublishedSourceDefersUntilPublicationAndRetiresOnWorldLeave()
    {
        // Arrange: Keep source geometry unavailable while resident pages request capture.
        EnsureContextValid();using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions) {GeometryAvailable=false};
        // Act: Let requests defer until the source is published.
        for(int frame=0;frame<16;frame++)runtime.Frame();
        // Assert: Deferred pages remain pending without GPU failures and cannot survive world leave.
        Assert.False(runtime.AllRequestedCaptured());Assert.Equal((0L,0L),CaptureCounts(runtime));
        AssertPendingResident(runtime);
        runtime.GeometryAvailable=true;
        runtime.RunUntil(()=>runtime.AllRequestedCaptured()&&runtime.TryGetLighting(out _));
        Assert.Equal(0,CaptureCounts(runtime).Failures);
        // Act: Leave the world after recovery to retire its lighting and feedback state.
        runtime.LeaveWorld();Assert.False(runtime.TryGetLighting(out _));
        // Assert: World leave retires both the published lighting and near-dispatch state.
        Assert.False(runtime.Feedback.TryGetNearDispatchState(out _,out _,out _,out _));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Reads CPU source-validation work separately from GPU capture attempts.</summary>
    private static long EligibilityChecks(SurfaceCacheRuntimeFixture runtime)
    {
        runtime.Feedback.TryGetSelfCheckLine(out string line);
        var match=Regex.Match(line,@"captureEligibilityChecks:(\d+)");Assert.True(match.Success,line);
        return long.Parse(match.Groups[1].Value);
    }

    /// <summary>Counts completed captures from the production mirror without treating pending residency as readiness.</summary>
    private static (int Resident,int Captured) CaptureStateCounts(SurfaceCacheRuntimeFixture runtime)
    {
        if(!runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out var mirror))return default;
        int captured=0;
        foreach(ulong key in mapping.Values)
        {
            int index=checked((int)LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key)*LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk+
                (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key));
            var flags=LumonScenePageTableEntryPacking.UnpackFlags(mirror[index]);
            if((flags&(LumonScenePageTableEntryPacking.Flags.NeedsCapture|LumonScenePageTableEntryPacking.Flags.Capturing))==0)captured++;
        }
        return(mapping.Count,captured);
    }

    /// <summary>Requires residency and NeedsCapture to survive deferred admission.</summary>
    private static void AssertPendingResident(SurfaceCacheRuntimeFixture runtime)
    {
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out var mirror));
        var page=Assert.Single(mapping);
        int index=checked((int)LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(page.Value)*LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk+
            (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(page.Value));
        var flags=LumonScenePageTableEntryPacking.UnpackFlags(mirror[index]);
        Assert.True(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.Resident));
        Assert.True(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.NeedsCapture));
        Assert.False(flags.HasFlag(LumonScenePageTableEntryPacking.Flags.Capturing));
    }

    /// <summary>Reads production capture attempt counters without consuming a GPU result or altering scheduling.</summary>
    private static (long Failures,long Attempts) CaptureCounts(SurfaceCacheRuntimeFixture runtime)
    {
        runtime.Feedback.TryGetSelfCheckLine(out string line);
        var match=Regex.Match(line,@"captureFail:(\d+)/(\d+)");Assert.True(match.Success,line);
        return(long.Parse(match.Groups[1].Value),long.Parse(match.Groups[2].Value));
    }
    #endregion
}
