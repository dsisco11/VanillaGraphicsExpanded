using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.PBR;
using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Distinguishes retained stale lighting from invalid or temporarily unavailable captured surface identities.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingIdentityTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API

    #region Source freshness
    /// <summary>Unchanged and light-only source refreshes retain capture and lighting after geometry becomes available again.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void SameSurfaceIdentityPreservesCaptureAndLighting(bool changeLight)
    {
        // Arrange
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        // Assert the published baseline before testing identity-preserving refresh.
        Assert.True(runtime.TryGetLighting(out var before));
        uint page=Pages(runtime).Single().Key;
        using var pixels=SurfaceLightingPageSelection.Capture(before.OutgoingRadiance,before,page);
        using var direct=SurfaceLightingPageSelection.Capture(before.DirectIrradiance,before,page);
        using var indirect=SurfaceLightingPageSelection.Capture(before.IndirectIrradiance,before,page);
        long captures=CaptureAttempts(runtime);
        // Act: invalidate only the source data or its light while holding publication work.
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 0;
        if(changeLight) runtime.ChangeBlockLight(0); else runtime.InvalidateGeometry();
        for(int frame=0;frame<16;frame++) runtime.Frame();
        // Assert: stale publication and every captured lighting plane remain unchanged.
        Assert.True(runtime.TryGetLighting(out var after));
        Assert.True(after.LightingIsStale);
        Assert.Equal(captures,CaptureAttempts(runtime));
        using var observed1=SurfaceLightingPageSelection.Capture(after.OutgoingRadiance,after,page);
        ImageAssertions.Comparisons.CompareHistory(observed1.Texture, pixels).AssertEqual();
        using var observed2=SurfaceLightingPageSelection.Capture(after.DirectIrradiance,after,page);
        ImageAssertions.Comparisons.CompareHistory(observed2.Texture, direct).AssertEqual();
        using var observed3=SurfaceLightingPageSelection.Capture(after.IndirectIrradiance,after,page);
        ImageAssertions.Comparisons.CompareHistory(observed3.Texture, indirect).AssertEqual();
        Assert.True(runtime.AllRequestedCaptured());
    }

    /// <summary>Unknown geometry suspends sampling without destroying retained history or forcing recapture on unchanged recovery.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void UnknownGeometrySuspendsAndResumesTheSameCapturedPage()
    {
        // Arrange
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        // Assert the captured-page baseline before suspending its source.
        Assert.True(runtime.TryGetLighting(out var before));
        uint page=Pages(runtime).Single().Key;
        using var pixels=SurfaceLightingPageSelection.Capture(before.OutgoingRadiance,before,page);
        long captures=CaptureAttempts(runtime);
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 0;
        // Act: make geometry unavailable while preventing relight from replacing history.
        runtime.GeometryAvailable=false;runtime.InvalidateGeometry();
        for(int frame=0;frame<4;frame++) runtime.Frame();
        // Assert the unresolved page is unavailable while its old outgoing history remains intact.
        Assert.True(runtime.TryGetLighting(out var suspended));
        Assert.False(runtime.Feedback.IsCaptureAvailable(page));
        using (var readiness=suspended.Readiness.MapRange<uint>(0,checked((int)page+1),BufferMapAccess.Read))
            Assert.Equal(0u,readiness.Span[(int)page]);
        // Act: issue a real query against the suspended capture.
        using (var queries=new SurfaceLightingQueryBatch(runtime.Api))
        {
            queries.Submit(runtime.Geometry.Resources!,suspended,
                [new(new(0,33,1),new(1,0,0),new(1,.5f,.5f),runtime.BlockId)]);
            GpuTestFence.WaitForGpuOrSkip("Suspended capture query");
            // Assert the suspended capture cannot produce a hit result.
            Assert.True(queries.TryRead(out var answers));
            Assert.Equal(0,answers[0].Result.W);
        }
        // Assert the retained page pixels, then make the same geometry available again.
        using var observed4=SurfaceLightingPageSelection.Capture(before.OutgoingRadiance,before,page);
        ImageAssertions.Comparisons.CompareHistory(observed4.Texture, pixels).AssertEqual();
        // Act
        runtime.GeometryAvailable=true;runtime.InvalidateGeometry();
        runtime.RunUntil(()=>runtime.Feedback.IsCaptureAvailable(page));
        // Assert that recovery reuses the captured surface without recapture.
        Assert.True(runtime.TryGetLighting(out var after));
        using var observed5=SurfaceLightingPageSelection.Capture(after.OutgoingRadiance,after,page);
        ImageAssertions.Comparisons.CompareHistory(observed5.Texture, pixels).AssertEqual();
        Assert.True(after.LightingIsStale);
        Assert.Equal(captures,CaptureAttempts(runtime));
    }

    /// <summary>A geometry edit in one patch does not recapture or clear another patch in the same source chunk.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void ChangedPatchInvalidatesOnlyItsOwnPage()
    {
        // Arrange
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(ImageAssertions, requestedPages:2,exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        // Assert and retain the two-page baseline before applying the localized edit.
        Assert.True(runtime.TryGetLighting(out var before));
        var pages=Pages(runtime).OrderBy(pair=>LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(pair.Value)).ToArray();
        using var untouched=SurfaceLightingPageSelection.Capture(before.OutgoingRadiance,before,pages[1].Key);
        long captures=CaptureAttempts(runtime);
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 0;
        // Act: invalidate one patch while relight budgets are held at zero.
        runtime.TransformVoxel=(x,y,z,voxel)=>x==0 && y>=32 && y<36 && z>=0 && z<4 ? voxel with { Geometry=1 } : voxel;
        runtime.InvalidateGeometry();
        for(int frame=0;frame<16;frame++) runtime.Frame();
        // Assert that only the changed page loses readiness and the untouched page keeps its pixels.
        Assert.True(runtime.TryGetLighting(out var after));
        using var observed6=SurfaceLightingPageSelection.Capture(after.OutgoingRadiance,after,pages[1].Key);
        ImageAssertions.Comparisons.CompareHistory(observed6.Texture, untouched).AssertEqual();
        Assert.Equal(captures+1,CaptureAttempts(runtime));
        using(var readiness=after.Readiness.MapRange<uint>(0,checked((int)pages.Max(pair=>pair.Key)+1),BufferMapAccess.Read))
        {
            Assert.Equal(0u,readiness.Span[(int)pages[0].Key]);
            Assert.Equal(1u,readiness.Span[(int)pages[1].Key]);
        }
        // Act: restore bounded work so the replacement page can publish.
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 1;
        runtime.RunUntil(runtime.AllRequestedLightingReady,maximumFrames:64);
        // Assert that the replacement is valid black and the untouched page still contains its captured light.
        Assert.True(runtime.TryGetLighting(out var recovered));
        using var removed=SurfaceLightingPageSelection.Capture(recovered.OutgoingRadiance,recovered,pages[0].Key);
        ImageAssertions.Comparisons.CompareConstant(removed.Texture, 0, channelMask: 7).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(removed.Texture, 1, channelMask: 8).AssertEqual();
        using var retained=SurfaceLightingPageSelection.Capture(recovered.DirectIrradiance,recovered,pages[1].Key);
        // The producer retains C-encoded RGB; this assertion compares physical irradiance.
        ImageAssertions.Comparisons.CompareConstant(retained.Texture, 32 * LightingStorage.PersistentScale, channelMask: 1).AssertEqual();
        Assert.Equal(captures+1,CaptureAttempts(runtime));
    }
    #endregion

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion

    #region Private
    #region Page observations
    /// <summary>Reads actual resident identities from the production feedback owner.</summary>
    private static IReadOnlyDictionary<uint,ulong> Pages(SurfaceCacheRuntimeFixture runtime)
    {
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));
        return pages;
    }

    /// <summary>Observes public capture diagnostics independently of source recapture work.</summary>
    private static long CaptureAttempts(SurfaceCacheRuntimeFixture runtime)
    {
        runtime.Feedback.TryGetSelfCheckLine(out string line);
        var match=Regex.Match(line,@"captureFail:\d+/(\d+)");
        Assert.True(match.Success,line);return long.Parse(match.Groups[1].Value);
    }
    #endregion
    #endregion
}
