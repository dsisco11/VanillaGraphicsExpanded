using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Exercises the production cache lookup and asynchronous transport against real captured and lit surfaces.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingConsumerTests : NearFieldShaderTestBase
{

    #region Public API
    /// <summary>Uses the isolated material registry and headless GPU context.</summary>
    public SurfaceLightingConsumerTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Hit lookup
    /// <summary>All six faces resolve across positive and negative chunk boundaries without another albedo factor.</summary>
    [Theory]
    [InlineData(0)] [InlineData(28)] [InlineData(-4)] [InlineData(-32)]
    public void FacesAndChunkBoundariesConsumeOutgoingRadiance(int offset)
    {
        // Arrange six authored face hits at the requested signed chunk origin.
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, .25f,xOffset:offset); room.Seed();
        var hits=new SurfaceLightingQuery[]
        {
            new(new(offset,35,3),new(1,0,0),new(1,.5f,.5f)),
            new(new(offset+7,35,3),new(-1,0,0),new(0,.5f,.5f)),
            new(new(offset+3,32,3),new(0,1,0),new(.5f,1,.5f)),
            new(new(offset+4,39,3),new(0,-1,0),new(0,0,.5f)),
            new(new(offset+3,35,0),new(0,0,1),new(.5f,.5f,1)),
            new(new(offset+4,35,7),new(0,0,-1),new(0,.5f,0))
        };
        // Act by querying all six authored faces through the production cache-query shader.
        // Assert every face returns a valid hit with the analytic outgoing radiance.
        foreach(var hit in Query(room,hits))
        {
            Assert.Equal(1,hit.Result.W);
            Assert.InRange(hit.Result.X,32*GgxHemisphereReference.CapturedDiffuse(.25f)/MathF.PI-.01f,32*GgxHemisphereReference.CapturedDiffuse(.25f)/MathF.PI+.01f);
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Black is ready; absent publication, stale slots and replaced block identities are not.</summary>
    [Fact]
    public void ValidBlackIsDistinctFromUnavailableOrStale()
    {
        // Arrange a published black surface, which is valid data rather than a missing-page sentinel.
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, 0); room.Seed();
        SurfaceLightingQuery hit=new(new(0,35,3),new(1,0,0),new(1,.5f,.5f));
        // Act first with a valid identity, then with a wrong identity and stale page slots.
        Assert.Equal(new Vector4(0,0,0,1),Query(room,[hit])[0].Result);
        // Assert absence and stale mappings cannot masquerade as valid black.
        var wrong=hit; wrong.BlockId=int.MaxValue;
        Assert.Equal(Vector4.Zero,Query(room,[wrong])[0].Result);
        room.StaleSlots();
        Assert.Equal(Vector4.Zero,Query(room,[hit])[0].Result);
    }

    /// <summary>Unready pages cannot be mistaken for valid zero radiance or returned from old atlas contents.</summary>
    [Fact]
    public void WithheldPagesRejectPreviouslyBrightLighting()
    {
        // Arrange a bright published hit before making its pages unavailable.
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions); room.Seed();
        SurfaceLightingQuery hit=new(new(0,35,3),new(1,0,0),new(1,.5f,.5f));
        // Act on the same hit before and after withdrawing its page readiness.
        // Assert the initial hit is bright and the identical hit is rejected after readiness is withdrawn.
        Assert.True(Query(room,[hit])[0].Result.X>1);
        room.WithholdPages();
        Assert.Equal(Vector4.Zero,Query(room,[hit])[0].Result);
    }
    #endregion

    #region Mapping and transport lifetime
    /// <summary>A ready physical tile belonging to another patch must not satisfy the requested surface.</summary>
    [Fact]
    public void AliasedPageMappingIsUnavailable()
    {
        // Arrange a ready hit whose physical tile can be remapped to another patch.
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);room.Seed();
        SurfaceLightingQuery hit=new(new(0,35,3),new(1,0,0),new(1,.5f,.5f));
        // Act by aliasing the first physical page to another logical surface.
        // Assert the logical hit resolves before aliasing and fails afterward.
        Assert.Equal(1,Query(room,[hit])[0].Result.W);
        room.AliasFirstPage();
        Assert.Equal(Vector4.Zero,Query(room,[hit])[0].Result);
    }

    /// <summary>Reusing an oversized allocation binds only the current batch and refuses overwrite while pending.</summary>
    [Fact]
    public void QueryBatchReuseRetainsExactAdmissionBounds()
    {
        // Arrange one reusable query batch and submit its initial two-hit allocation.
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);room.Seed();
        using var assets=new BinaryShaderApiFixture();
        using var batch=new SurfaceLightingQueryBatch(assets.Api);
        SurfaceLightingQuery hit=new(new(0,35,3),new(1,0,0),new(1,.5f,.5f));
        // Act on pending, completed, and reused batch submissions.
        batch.Submit(room.Geometry.Scene,room.Snapshot,new[]{hit,hit});
        // Assert pending submissions reject overwrite, and reused batches return only their current admitted results.
        Assert.Throws<InvalidOperationException>(()=>batch.Submit(room.Geometry.Scene,room.Snapshot,new[]{hit}));
        GpuTestFence.WaitForGpuOrSkip("First hit batch");
        Assert.True(batch.TryRead(out var first));Assert.Equal(2,first.Length);
        batch.Submit(room.Geometry.Scene,room.Snapshot,new[]{hit});
        GpuTestFence.WaitForGpuOrSkip("Reused hit batch");
        Assert.True(batch.TryRead(out var second));Assert.Single(second);Assert.Equal(1,second[0].Result.W);
        Assert.False(batch.TryRead(out _));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Screen probe consumption
    /// <summary>Real offscreen traces read produced outgoing radiance, while missing pages retain opaque distances at zero confidence.</summary>
    [Theory]
    [InlineData(0f,32)] [InlineData(.25f,32)] [InlineData(.25f,192)] [InlineData(.25f,256)]
    public void ScreenProbesConsumePublishedCache(float albedo,int surfaceResolution)
    {
        // Arrange a seeded cache and capture a real screen trace using the selected material parameters.
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, albedo,surfaceResolution:surfaceResolution); room.Seed();
        using var placeholder=new NearFieldVoxelFixture();
        // Act with ready pages, then withhold them and repeat the identical trace.
        var lit=Trace(placeholder,shared:room.Geometry.Scene,surfaceLighting:room.Snapshot,anchorPosition:new(4,36,4));

        float expected=32*GgxHemisphereReference.CapturedDiffuse(albedo)/MathF.PI;
        var hitObservation=ObserveTrace(lit, SurfacePredicates.SurfaceLightingConsumerTests_ScreenProbesConsumePublishedCache,
            parameters:new(expected-.02f,expected+.02f,0,0));
        // Assert ready pages yield analytic radiance and missing pages preserve the opaque, zero-confidence result.
        hitObservation.AssertEqual(); uint hits=hitObservation.MatchingTexels;
        Assert.True(hits>32);
        room.WithholdPages();
        var missing=Trace(placeholder,shared:room.Geometry.Scene,surfaceLighting:room.Snapshot,anchorPosition:new(4,36,4));
        ObserveTrace(missing, SurfacePredicates.SurfaceLightingConsumerTests_ScreenProbesConsumePublishedCache_2,lit).AssertEqual();
    }
    #endregion

    #region Screen hit continuity
    /// <summary>Accepted screen hits and offscreen hits consume identical cached emission without reapplying boost or albedo.</summary>
    [Fact]
    public void ScreenHitsPreserveCachedEmissionExactlyOnce()
    {
        // Arrange a zero-diffuse receiver with one authored cached emission contribution.
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, 0,32,12){EmissionPolicy=true};room.Seed();
        using var placeholder=new NearFieldVoxelFixture();
        var projection=LumOnTestInputFactory.CreateRealisticProjection();
        float z=-8, depth=(projection[10]*z+projection[14])/(projection[11]*z+projection[15])*.5f+.5f;
        // Act with a control screen hit and the equivalent offscreen cache-backed hit.
        var control=Trace(placeholder,screenDepth:depth,nearFieldTracing:false);
        var actual=Trace(placeholder,screenDepth:depth,screenEmission:8,emissionBoost:3,
            shared:room.Geometry.Scene,surfaceLighting:room.Snapshot,worldOffset:new(0,32,0),matrixRemainder:new(4,4,9));

        var hitObservation=ObserveTrace(actual, SurfacePredicates.SurfaceLightingConsumerTests_ScreenHitsPreserveCachedEmissionExactlyOnce,control);
        // Assert both paths agree without reapplying material albedo or emission boost.
        hitObservation.AssertEqual(); uint hits=hitObservation.MatchingTexels;
        Assert.True(hits>0);
    }
    #endregion

    #region CPU world-probe transport
    /// <summary>Worker-produced hits resolve through the real GPU cache while preserving signed distances and occlusion.</summary>
    [Fact]
    public void CpuWorldProbeHitsResolveThroughPublishedCache()
    {
        // Arrange real CPU tracing over controlled geometry and obtain its surface-hit descriptors.
        EnsureShaderTestAvailable();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);room.Seed();
        var world=new VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures.ControlledVoxelWorld();
        world.AddRoom((0,32,0),(7,39,7),materialId:room.BlockId);
        var request=new VanillaGraphicsExpanded.LumOn.WorldProbes.LumOnWorldProbeUpdateRequest(0,new(),new(),0);
        var work=new VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing.LumOnWorldProbeTraceWorkItem(
            0,request,new(4,36,4),64,8,64,false,.25f,-1,1e-6f,DeferSurfaceLighting:true);
        // Act by resolving worker-produced hits through the real published GPU cache, then withdraw its pages.
        var traced=new VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing.LumOnWorldProbeTraceIntegrator()
            .TraceProbe(world.CreateTraceScene(),work,CancellationToken.None);
        Assert.True(traced.Success);
        var descriptors=traced.AtlasSamples.Select(s=>s.SurfaceHit!.Value).ToArray();
        var answers=Query(room,descriptors);
        int index=0;
        var resolved=VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing.WorldProbeSurfaceLighting.Resolve(traced,answers,ref index);
        // Assert resolved radiance and signed distances, then require unavailable pages to reject the trace.
        Assert.True(resolved.Success);Assert.Equal(64,index);
        for(int i=0;i<64;i++)
        {
            float expected=32*GgxHemisphereReference.CapturedDiffuse(.25f)/MathF.PI; Assert.InRange(resolved.AtlasSamples[i].RadianceRgb.X,expected-.02f,expected+.02f);
            Assert.Equal(traced.AtlasSamples[i].AlphaEncodedDistSigned,resolved.AtlasSamples[i].AlphaEncodedDistSigned);
        }
        room.WithholdPages();answers=Query(room,descriptors);index=0;
        Assert.False(VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing.WorldProbeSurfaceLighting.Resolve(traced,answers,ref index).Success);
    }
    #endregion
    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion

    #region Private
    #region Query execution
    /// <summary>Waits only in the test harness; production polls the same query owner on subsequent frames.</summary>
    private static SurfaceLightingQuery[] Query(SurfaceLightingEnclosureFixture room, SurfaceLightingQuery[] hits)
    {
        using var assets=new BinaryShaderApiFixture();
        using var batch=new SurfaceLightingQueryBatch(assets.Api);
        batch.Submit(room.Geometry.Scene,room.Snapshot,hits);
        GpuTestFence.WaitForGpuOrSkip("Surface hit queries");
        Assert.True(batch.TryRead(out var answers));
        return answers;
    }
    #endregion
    #endregion
}



