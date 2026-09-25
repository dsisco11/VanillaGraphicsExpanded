using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Distinguishes retained stale lighting from invalid or temporarily unavailable captured surface identities.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingIdentityTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Source freshness
    /// <summary>Unchanged and light-only source refreshes retain capture and lighting after geometry becomes available again.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SameSurfaceIdentityPreservesCaptureAndLighting(bool changeLight)
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var before));
        uint page=Pages(runtime).Single().Key;
        using var pixels=SurfaceLightingPageReadback.Read(before.OutgoingRadiance,before,page);
        using var direct=SurfaceLightingPageReadback.Read(before.DirectIrradiance,before,page);
        using var indirect=SurfaceLightingPageReadback.Read(before.IndirectIrradiance,before,page);
        long captures=CaptureAttempts(runtime);
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame=0;
        if(changeLight) runtime.ChangeBlockLight(0); else runtime.InvalidateGeometry();
        for(int frame=0;frame<16;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var after));
        Assert.True(after.LightingIsStale);
        Assert.Equal(captures,CaptureAttempts(runtime));
        using var observed1=SurfaceLightingPageReadback.Read(after.OutgoingRadiance,after,page);
        Assert.True(pixels.Span.SequenceEqual(observed1.Span));
        using var observed2=SurfaceLightingPageReadback.Read(after.DirectIrradiance,after,page);
        Assert.True(direct.Span.SequenceEqual(observed2.Span));
        using var observed3=SurfaceLightingPageReadback.Read(after.IndirectIrradiance,after,page);
        Assert.True(indirect.Span.SequenceEqual(observed3.Span));
        Assert.True(runtime.AllRequestedCaptured());
    }

    /// <summary>Unknown geometry suspends sampling without destroying retained history or forcing recapture on unchanged recovery.</summary>
    [Fact]
    public void UnknownGeometrySuspendsAndResumesTheSameCapturedPage()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var before));
        uint page=Pages(runtime).Single().Key;
        using var pixels=SurfaceLightingPageReadback.Read(before.OutgoingRadiance,before,page);
        long captures=CaptureAttempts(runtime);
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame=0;
        runtime.GeometryAvailable=false;runtime.InvalidateGeometry();
        for(int frame=0;frame<4;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var suspended));
        Assert.False(runtime.Feedback.IsCaptureAvailable(page));
        using (var readiness=suspended.Readiness.MapRange<uint>(0,checked((int)page+1),OpenTK.Graphics.OpenGL.MapBufferAccessMask.MapReadBit))
            Assert.Equal(0u,readiness.Span[(int)page]);
        using (var queries=new SurfaceLightingQueryBatch(runtime.Api))
        {
            queries.Submit(runtime.Geometry.Resources!,suspended,
                [new(new(0,33,1),new(1,0,0),new(1,.5f,.5f),runtime.BlockId)]);
            GpuTestFence.WaitForGpuOrSkip("Suspended capture query");
            Assert.True(queries.TryRead(out var answers));
            Assert.Equal(0,answers[0].Result.W);
        }
        using var observed4=SurfaceLightingPageReadback.Read(before.OutgoingRadiance,before,page);
        Assert.True(pixels.Span.SequenceEqual(observed4.Span));
        runtime.GeometryAvailable=true;runtime.InvalidateGeometry();
        runtime.RunUntil(()=>runtime.Feedback.IsCaptureAvailable(page));
        Assert.True(runtime.TryGetLighting(out var after));
        using var observed5=SurfaceLightingPageReadback.Read(after.OutgoingRadiance,after,page);
        Assert.True(pixels.Span.SequenceEqual(observed5.Span));
        Assert.True(after.LightingIsStale);
        Assert.Equal(captures,CaptureAttempts(runtime));
    }

    /// <summary>A geometry edit in one patch does not recapture or clear another patch in the same source chunk.</summary>
    [Fact]
    public void ChangedPatchInvalidatesOnlyItsOwnPage()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(requestedPages:2,exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var before));
        var pages=Pages(runtime).OrderBy(pair=>LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(pair.Value)).ToArray();
        using var untouched=SurfaceLightingPageReadback.Read(before.OutgoingRadiance,before,pages[1].Key);
        long captures=CaptureAttempts(runtime);
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame=0;
        runtime.TransformVoxel=(x,y,z,voxel)=>x==0 && y>=32 && y<36 && z>=0 && z<4 ? voxel with { Geometry=1 } : voxel;
        runtime.InvalidateGeometry();
        for(int frame=0;frame<16;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var after));
        using var observed6=SurfaceLightingPageReadback.Read(after.OutgoingRadiance,after,pages[1].Key);
        Assert.True(untouched.Span.SequenceEqual(observed6.Span));
        Assert.Equal(captures+1,CaptureAttempts(runtime));
        using(var readiness=after.Readiness.MapRange<uint>(0,checked((int)pages.Max(pair=>pair.Key)+1),OpenTK.Graphics.OpenGL.MapBufferAccessMask.MapReadBit))
        {
            Assert.Equal(0u,readiness.Span[(int)pages[0].Key]);
            Assert.Equal(1u,readiness.Span[(int)pages[1].Key]);
        }
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame=1;
        runtime.RunUntil(runtime.AllRequestedLightingReady,maximumFrames:64);
        Assert.True(runtime.TryGetLighting(out var recovered));
        using var removed=SurfaceLightingPageReadback.Read(recovered.OutgoingRadiance,recovered,pages[0].Key);
        for(int index=0;index<removed.Length;index++) Assert.Equal((index&3)==3?1:0,removed.Span[index]);
        using var retained=SurfaceLightingPageReadback.Read(recovered.DirectIrradiance,recovered,pages[1].Key);
        for(int index=0;index<retained.Length;index+=4) Assert.Equal(32,retained.Span[index]);
        Assert.Equal(captures+1,CaptureAttempts(runtime));
    }
    #endregion

    #region Observations
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
}
