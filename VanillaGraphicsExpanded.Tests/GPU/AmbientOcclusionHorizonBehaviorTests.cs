using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies raw horizons and both spatial stages across projection, distance and visible screen coverage.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AmbientOcclusionHorizonBehaviorTests(HeadlessGLFixture fixture,ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    /// <summary>Analytic planes remain neutral; wall contact survives varied projections and distances, while resolved near corners survive spatial reconstruction.</summary>
    [Theory]
    [InlineData(60f,3f)] [InlineData(60f,20f)]
    [InlineData(90f,10f)]
    [InlineData(110f,3f)] [InlineData(110f,20f)]
    public void ProjectionAndDistancePreserveReceiverEvidence(float fov,float distance)
    {
        EnsureShaderTestAvailable();
        using var owner=new AmbientOcclusionPass();using var draw=new PostprocessDraw();
        foreach(int quality in new[]{1,2})foreach(int scene in new[]{0,1,4,5,9}) {
            using var inputs=new AmbientOcclusionSceneFixture(ImageAssertions, TestFramework,Programs,640,360,scene,receiverDistance:distance,verticalFov:fov);
            inputs.Render(owner,draw,quality);
            using var stages=AmbientOcclusionSceneFixture.CaptureStages(owner);
            AssertStage(stages.Raw);AssertStage(stages.Filtered);AssertStage(stages.Reconstructed);
            float raw=Minimum(stages.Raw),filtered=Minimum(stages.Filtered),final=Minimum(stages.Reconstructed);
            log.WriteLine($"Horizon fov={fov} distance={distance} quality={quality} scene={scene}: rawMin={raw:R}, filteredMin={filtered:R}, finalMin={final:R}");
            if(scene<=1) {
                Assert.InRange(raw,.995f,1.001f);Assert.InRange(filtered,.995f,1.001f);Assert.InRange(final,.995f,1.001f);
            } else if(scene==4||(scene==5&&distance==3)) {
                Assert.True(raw<.995f,$"Missing raw contact evidence at fov={fov}, distance={distance}, scene={scene}, quality={quality}");
                Assert.True(final<.995f,$"Spatial reconstruction lost contact evidence at fov={fov}, distance={distance}, scene={scene}, quality={quality}");
            }
            // Distant corner contact is not reliably retained in this fixture; check its bounds without claiming surviving contact or a proven cause.
            if(scene==4) {
                float reconstructionHalo=6*(2*distance*MathF.Tan(fov*MathF.PI/360)/360);
                ImageAssertions.Predicates.Predicate(stages.Reconstructed, AoPredicates.AmbientOcclusionHorizonBehaviorTests_ProjectionAndDistancePreserveReceiverEvidence,inputs.Position,parameters:new(1.25f+reconstructionHalo,0,0,0)).AssertEqual();
            }
            // Invert the independent authored perspective relation only at the original sparse receiver grid.
            var depthAgreement=ImageAssertions.Predicates.Predicate(stages.Reconstructed, AoPredicates.AmbientOcclusionHorizonBehaviorTests_ProjectionAndDistancePreserveReceiverEvidence_2,inputs.Depth);
            depthAgreement.AssertEqual();Assert.Equal(216u,depthAgreement.MatchingTexels);
        }
    }

    /// <summary>Clipped rays never invent occlusion; visible contact can still occlude near either image boundary.</summary>
    [Theory]
    [InlineData(0f)] [InlineData(.05f)] [InlineData(.95f)] [InlineData(1f)]
    public void ScreenEdgesUseOnlyVisibleGeometry(float edgeFraction)
    {
        EnsureShaderTestAvailable();using var owner=new AmbientOcclusionPass();using var draw=new PostprocessDraw();
        const int width=257,height=129;
        foreach(int quality in new[]{1,2}) {
            using var inputs=new AmbientOcclusionSceneFixture(ImageAssertions, TestFramework,Programs,width,height,4,edgeFraction:edgeFraction);
            inputs.Render(owner,draw,quality);using var stages=AmbientOcclusionSceneFixture.CaptureStages(owner);
            AssertStage(stages.Raw);AssertStage(stages.Filtered);AssertStage(stages.Reconstructed);
            float final=Minimum(stages.Reconstructed);
            if(edgeFraction==0||edgeFraction==1)Assert.InRange(final,.995f,1.001f);
            else {
                Assert.True(final<.995f,$"Visible edge contact disappeared at {edgeFraction}, quality{quality}: {final:R}");
                // The foreground is a flat surface with no geometry in front of it, including the clipped image boundary.
                int edge=(int)(width*edgeFraction);
                ImageAssertions.Predicates.Predicate(stages.Reconstructed, AoPredicates.AmbientOcclusionHorizonBehaviorTests_ScreenEdgesUseOnlyVisibleGeometry,
                    parameters:new(Math.Max(0,edge-2),0,0,0)).AssertEqual();
            }
            log.WriteLine($"Screen edge fraction={edgeFraction} quality={quality}: rawMin={Minimum(stages.Raw):R}, finalMin={final:R}");
        }
    }

    /// <summary>Receivers beyond the world-distance fade publish neutral visibility and no reusable AO depth.</summary>
    [Theory]
    [InlineData(90f)]
    public void FarReceiversWithdrawVisibilityAtEveryProjection(float fov)
    {
        EnsureShaderTestAvailable();using var owner=new AmbientOcclusionPass();using var draw=new PostprocessDraw();
        using var inputs=new AmbientOcclusionSceneFixture(ImageAssertions, TestFramework,Programs,129,73,7,verticalFov:fov);
        inputs.Render(owner,draw,2);using var stages=AmbientOcclusionSceneFixture.CaptureStages(owner);
        foreach(var stage in new[]{stages.Raw,stages.Filtered,stages.Reconstructed})
        {
            ImageAssertions.Comparisons.CompareConstant(stage,1,channelMask:1).AssertEqual();
            ImageAssertions.Comparisons.CompareConstant(stage,0,channelMask:2).AssertEqual();
        }
    }

    /// <summary>Compares quality at identical geometry, allocation and pass count using warm ABBA submissions.</summary>
    [Theory]
    [InlineData(1280,720)] [InlineData(1920,1080)]
    [Trait("Purpose","Diagnostic")]
    public void MatchedWorkloadQualityMeasurements(int width,int height)
    {
        EnsureShaderTestAvailable();using var owner=new AmbientOcclusionPass();using var draw=new PostprocessDraw();
        using var inputs=new AmbientOcclusionSceneFixture(ImageAssertions, TestFramework,Programs,width,height,4);
        var gpu=new[]{new List<double>(),new List<double>()};var cpu=new[]{new List<double>(),new List<double>()};
        for(int repeat=0;repeat<3;repeat++)foreach(int quality in new[]{1,2,2,1}) {
            var sample=inputs.Measure(owner,draw,quality,1);gpu[quality-1].Add(sample.Gpu[0]);cpu[quality-1].Add(sample.Cpu[0]);
        }
        for(int quality=1;quality<=2;quality++)log.WriteLine($"Matched AO {width}x{height} quality{quality}: six ABBA samples GPU median={gpu[quality-1].Order().Skip(2).Take(2).Average():F4}ms range={gpu[quality-1].Min():F4}..{gpu[quality-1].Max():F4}; CPU submission median={cpu[quality-1].Order().Skip(2).Take(2).Average():F4}ms; AO bytes={owner.StorageBytes}; HZB bytes={inputs.HierarchyBytes}; one hierarchy dispatch + up to four mask draws + three AO draws; candidates={(quality==1?24:72)} per half-resolution pixel. Excludes target creation/readback/query waits; not live frame cost.");
        using var stages=AmbientOcclusionSceneFixture.CaptureStages(owner);AssertStage(stages.Reconstructed);
    }
    #endregion

    #region Private
    /// <summary>Finds visibility without mixing depth or encoded normal channels.</summary>
    private float Minimum(GpuTexture image)=>(float)ImageAssertions.Statistics.Inspect(image,channelMask:1).Minimum;

    /// <summary>Rejects nonfinite intermediates and out-of-range visibility at every spatial stage.</summary>
    private void AssertStage(GpuTexture image)
    {
        ImageAssertions.Statistics.Inspect(image).AssertFiniteRange();
        ImageAssertions.Statistics.Inspect(image,0,1.001f,channelMask:1).AssertFiniteRange();
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-ao", 0, 0);
    }
    #endregion
}
