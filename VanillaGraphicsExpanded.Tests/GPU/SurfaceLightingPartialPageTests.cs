using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises partial cache publication using production capture, seed, scheduling and atlas ownership.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingPartialPageTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Partial seed publication
    /// <summary>Partial lighting support never relaxes the requirement for a coherent complete material capture.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void UnresolvedCapturedSourceKeepsEntirePageUnavailable()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.TransformVoxel=(x,y,z,voxel)=>x==0 && y>=34 ? voxel with { Geometry=0 } : voxel;
        runtime.PrimeGeometry();
        for(int frame=0;frame<16;frame++) runtime.Frame();
        Assert.False(runtime.AllRequestedCaptured());
        Assert.False(runtime.TryGetLighting(out _));
        runtime.Feedback.TryGetSelfCheckLine(out string diagnostics);
        Assert.Contains("captureFail:0/0",diagnostics);
        Assert.Matches(@"captureDeferredChecks:[1-9]\d*",diagnostics);
    }

    /// <summary>Unsupported neighboring geometry cannot suppress valid texels on the same fully captured page.</summary>
    [Theory]
    [InlineData(1)] [InlineData(4)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void ReadySeedTexelsPublishWhileUnresolvedNeighborsRetry(int density)
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge=density;
        runtime.Config.LumOn.LumonScene.RelightTexelsPerPagePerFrame=density*density<<2;
        runtime.TransformVoxel=(x,y,z,voxel)=>x==1 && y>=34 ? voxel with { Geometry=3 } : voxel;
        runtime.PrimeGeometry();
        runtime.RunUntil(()=>runtime.TryGetLighting(out _));
        Assert.True(runtime.AllRequestedCaptured());
        Assert.DoesNotContain(runtime.Events.Registrations,r=>r.Name=="vge_worldprobe_update");
        for(int frame=0;frame<16;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var snapshot));
        using var pixelsOwner=CaptureFirstPage(runtime,snapshot.OutgoingRadiance,snapshot);
        var pixels=pixelsOwner.Texture;
        Assert.Equal((density*density)<<3,CountValidity(pixels,1));
        Assert.Equal((density*density)<<3,CountValidity(pixels,0));
        ImageAssertions.Inspect(pixels).AssertFiniteRange();
        ImageAssertions.InspectCoverage(pixels, MathF.BitIncrement(.001f), double.PositiveInfinity, channelMask: 1).AssertFiniteRange();
        ImageAssertions.InspectCoverage(pixels, double.NegativeInfinity, double.PositiveInfinity, channelMask: 6).AssertFiniteRange();
        using var queries=new SurfaceLightingQueryBatch(runtime.Api);
        queries.Submit(runtime.Geometry.Resources!,snapshot,
            [new(new(0,33,1),new(1,0,0),new(1,.5f,.5f),runtime.BlockId),
             new(new(0,35,1),new(1,0,0),new(1,.5f,.5f),runtime.BlockId)]);
        GpuTestFence.WaitForGpuOrSkip("Partial surface page hit queries");
        Assert.True(queries.TryRead(out var answers));
        Assert.Equal(1,answers[0].Result.W);Assert.True(answers[0].Result.X>.001f);
        Assert.Equal(0,answers[1].Result.W);
    }

    /// <summary>A real source invalidation resets prior lighting before publishing a different partial valid region.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void SourceChangeCannotExposePriorValidTexelsInNewPartialPage()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        runtime.PrimeGeometry();runtime.RunUntil(()=>runtime.TryGetLighting(out _));
        Assert.True(runtime.TryGetLighting(out var before));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));
        uint page=pages.Single().Key;
        long capture=runtime.Feedback.GetCaptureRevision(page);
        runtime.TransformVoxel=(x,y,z,voxel)=>x==0 && y>=34 ? voxel with { Geometry=1 } : x==1 && y<34 ? voxel with { Geometry=3 } : voxel;
        runtime.ChangeBlockLight(0);
        for(int frame=0;frame<24;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var snapshot));
        Assert.True(runtime.Feedback.GetCaptureRevision(page)>capture);
        Assert.Equal(before.DependencyRevision,snapshot.DependencyRevision);
        using var pixelsOwner=CaptureFirstPage(runtime,snapshot.OutgoingRadiance,snapshot);
        var pixels=pixelsOwner.Texture;
        Assert.Equal(8,CountValidity(pixels,1));
        Assert.Equal(8,CountValidity(pixels,0));
        ImageAssertions.CompareConstant(pixels, 0, channelMask: 7).AssertEqual();
    }
    #endregion

    #region Partial indirect publication
    /// <summary>Short ray budgets leave some directions unresolved while successful nearby bounces still reach published outgoing light.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void MixedIndirectTraceOutcomesPublishSuccessfulTexels()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(requestedPages:24,enclosure:true);
        runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        runtime.Config.LumOn.LumonScene.RelightMaxDdaSteps=3;
        runtime.PrimeGeometry();runtime.RunUntil(runtime.AllRequestedLightingReady);
        for(int frame=0;frame<48;frame++) runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var snapshot));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        bool publishedBounce=false;
        foreach(uint physical in mapping.Keys)
        {
            using var outgoingOwner=SurfaceLightingPageSelection.Capture(snapshot.OutgoingRadiance,snapshot,physical);
            var outgoing=outgoingOwner.Texture;
            using var directOwner=SurfaceLightingPageSelection.Capture(snapshot.DirectIrradiance,snapshot,physical);
            var direct=directOwner.Texture;
            using var indirectOwner=SurfaceLightingPageSelection.Capture(snapshot.IndirectIrradiance,snapshot,physical);
            var indirect=indirectOwner.Texture;
            ImageAssertions.CompareConstant(outgoing, 1, channelMask: 8).AssertEqual();
            ImageAssertions.CompareRelation(outgoing, direct, GgxHemisphereReference.CapturedDiffuse(1),
                -.01f, float.PositiveInfinity, channelMask: 1, divisor: MathF.PI).AssertEqual();
            publishedBounce |= ImageAssertions.CountExcess(outgoing, direct, indirect,
                GgxHemisphereReference.CapturedDiffuse(1), .001f, .001f, divisor: MathF.PI).MatchingTexels > 0;
        }
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string diagnostics);
        Assert.Matches(@"indirectFail:[1-9]\d*/",diagnostics);
        Assert.True(publishedBounce,"Successful texels remained trapped in unpublished indirect scratch history: "+diagnostics);
    }
    #endregion

    #region Atlas observations
    /// <summary>Counts the exact authored validity value over the complete captured page.</summary>
    private int CountValidity(GpuTexture image, float value) =>
        checked((int)ImageAssertions.CompareConstant(image, value, channelMask: 8).ConstantMatches);
    /// <summary>Captures one real resident tile independently of later producer writes.</summary>
    private static Assertions.GpuImageSnapshot CaptureFirstPage(SurfaceCacheRuntimeFixture runtime,Texture3D texture,in SurfaceLightingSnapshot snapshot)
    {
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        return SurfaceLightingPageSelection.Capture(texture,snapshot,mapping.Keys.Min());
    }

    #endregion
}
