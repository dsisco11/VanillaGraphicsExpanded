using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies raw horizons and both spatial stages across projection, distance and visible screen coverage.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class AmbientOcclusionHorizonBehaviorTests(HeadlessGLFixture fixture,ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Analytic planes remain neutral; wall contact survives varied projections and distances, while resolved near corners survive spatial reconstruction.</summary>
    [Theory]
    [InlineData(60f,3f)] [InlineData(60f,10f)] [InlineData(60f,20f)]
    [InlineData(90f,3f)] [InlineData(90f,10f)] [InlineData(90f,20f)]
    [InlineData(110f,3f)] [InlineData(110f,10f)] [InlineData(110f,20f)]
    public void ProjectionAndDistancePreserveReceiverEvidence(float fov,float distance)
    {
        EnsureShaderTestAvailable();
        using var owner=new AmbientOcclusionPass();using var draw=new PostprocessDraw();
        foreach(int quality in new[]{1,2})foreach(int scene in new[]{0,1,4,5,9}) {
            using var inputs=new AmbientOcclusionSceneFixture(TestFramework,Programs,640,360,scene,receiverDistance:distance,verticalFov:fov);
            inputs.Render(owner,draw,quality);
            var stages=AmbientOcclusionSceneFixture.ReadStages(owner);
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
                float[] positions=inputs.Position.ReadPixels();
                float reconstructionHalo=6*(2*distance*MathF.Tan(fov*MathF.PI/360)/360);
                // The foreground half-plane ends at view-space x=0. Beyond radius plus the spatial footprint, no front point can occlude.
                for(int pixel=0;pixel<640*360;pixel++)if(positions[pixel*4]>1.25f+reconstructionHalo)
                    Assert.InRange(stages.Reconstructed[pixel*4],.995f,1.001f);
            }
            // Final receiver depth must agree with its real geometry, even when a reduced footprint chose another leaf.
            float[] depthPixels=inputs.Depth.ReadPixels();
            for(int y=0;y<360;y+=31)for(int x=0;x<640;x+=37) {
                int pixel=y*640+x;float hardware=depthPixels[pixel];
                float expected=DepthFromHardware(hardware);
                Assert.InRange(stages.Reconstructed[pixel*4+1],expected-.04f,expected+.04f);
            }
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
            using var inputs=new AmbientOcclusionSceneFixture(TestFramework,Programs,width,height,4,edgeFraction:edgeFraction);
            inputs.Render(owner,draw,quality);var stages=AmbientOcclusionSceneFixture.ReadStages(owner);
            AssertStage(stages.Raw);AssertStage(stages.Filtered);AssertStage(stages.Reconstructed);
            float final=Minimum(stages.Reconstructed);
            if(edgeFraction==0||edgeFraction==1)Assert.InRange(final,.995f,1.001f);
            else {
                Assert.True(final<.995f,$"Visible edge contact disappeared at {edgeFraction}, quality{quality}: {final:R}");
                // The foreground is a flat surface with no geometry in front of it, including the clipped image boundary.
                int edge=(int)(width*edgeFraction);
                for(int y=0;y<height;y++)for(int x=0;x<Math.Max(0,edge-2);x++)Assert.InRange(stages.Reconstructed[(y*width+x)*4],.995f,1.001f);
            }
            log.WriteLine($"Screen edge fraction={edgeFraction} quality={quality}: rawMin={Minimum(stages.Raw):R}, finalMin={final:R}");
        }
    }

    /// <summary>Receivers beyond the world-distance fade publish neutral visibility and no reusable AO depth.</summary>
    [Theory]
    [InlineData(60f)] [InlineData(90f)] [InlineData(110f)]
    public void FarReceiversWithdrawVisibilityAtEveryProjection(float fov)
    {
        EnsureShaderTestAvailable();using var owner=new AmbientOcclusionPass();using var draw=new PostprocessDraw();
        using var inputs=new AmbientOcclusionSceneFixture(TestFramework,Programs,129,73,7,verticalFov:fov);
        inputs.Render(owner,draw,2);var stages=AmbientOcclusionSceneFixture.ReadStages(owner);
        foreach(float[] stage in new[]{stages.Raw,stages.Filtered,stages.Reconstructed})for(int i=0;i<stage.Length;i+=4) {
            Assert.Equal(1,stage[i]);Assert.Equal(0,stage[i+1]);
        }
    }

    /// <summary>Compares quality at identical geometry, allocation and pass count using warm ABBA submissions.</summary>
    [Theory]
    [InlineData(1280,720)] [InlineData(1920,1080)]
    public void MatchedWorkloadQualityMeasurements(int width,int height)
    {
        EnsureShaderTestAvailable();using var owner=new AmbientOcclusionPass();using var draw=new PostprocessDraw();
        using var inputs=new AmbientOcclusionSceneFixture(TestFramework,Programs,width,height,4);
        var gpu=new[]{new List<double>(),new List<double>()};var cpu=new[]{new List<double>(),new List<double>()};
        for(int repeat=0;repeat<3;repeat++)foreach(int quality in new[]{1,2,2,1}) {
            var sample=inputs.Measure(owner,draw,quality,1);gpu[quality-1].Add(sample.Gpu[0]);cpu[quality-1].Add(sample.Cpu[0]);
        }
        for(int quality=1;quality<=2;quality++)log.WriteLine($"Matched AO {width}x{height} quality{quality}: six ABBA samples GPU median={gpu[quality-1].Order().Skip(2).Take(2).Average():F4}ms range={gpu[quality-1].Min():F4}..{gpu[quality-1].Max():F4}; CPU submission median={cpu[quality-1].Order().Skip(2).Take(2).Average():F4}ms; AO bytes={owner.StorageBytes}; HZB bytes={inputs.HierarchyBytes}; one hierarchy dispatch + three AO draws; candidates={(quality==1?24:72)} per half-resolution pixel. Excludes target creation/readback/query waits; not live frame cost.");
        AssertStage(AmbientOcclusionSceneFixture.ReadStages(owner).Reconstructed);
    }
    #endregion

    #region Private
    /// <summary>Independently inverts the authored near/far perspective depth relation.</summary>
    private static float DepthFromHardware(float depth)=>20f/(100.1f-(depth*2-1)*99.9f);

    /// <summary>Finds visibility without mixing depth or encoded normal channels.</summary>
    private static float Minimum(float[] pixels)=>pixels.Where((_,i)=>i%4==0).Min();

    /// <summary>Rejects nonfinite intermediates and out-of-range visibility at every spatial stage.</summary>
    private static void AssertStage(float[] pixels)
    {
        Assert.All(pixels,value=>Assert.True(float.IsFinite(value)));
        foreach(float visibility in pixels.Where((_,i)=>i%4==0))Assert.InRange(visibility,0,1.001f);
    }
    #endregion
}
