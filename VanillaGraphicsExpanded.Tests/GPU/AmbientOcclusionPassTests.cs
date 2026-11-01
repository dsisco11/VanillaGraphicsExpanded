using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the owned horizon pass with independent analytic receiver geometry and no temporal history.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AmbientOcclusionPassTests(HeadlessGLFixture fixture, ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Unoccluded flat and tilted planes, clear sky and invalid receivers remain neutral including image boundaries.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    public void UnoccludedReceiversRemainNeutral(int scene)
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        using var result = RenderAndCapture(owner, draw, 65, 37, 2, scene);
        AssertFinite(result);
        if(scene==7)ImageAssertions.CompareConstant(result.Texture,0,channelMask:2).AssertEqual();
        ImageAssertions.Inspect(result.Texture,.995f,1.001f,channelMask:1).AssertFiniteRange();
    }

    /// <summary>Nearby foreground darkens a background receiver, transmission attenuates once, and the next plane frame has no residual history.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ContactTransmissionAndMotionRemainCurrentFrame(int quality)
    {
        EnsureShaderTestAvailable();
        using var declarations = new StaticPassDeclarationCapture();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        using var opaque = RenderAndCapture(owner, draw, 65, 37, quality, 4);
        using var transparentOccluder = RenderAndCapture(owner, draw, 65, 37, quality, 4, 1, 0);
        using var partialReceiver = RenderAndCapture(owner, draw, 65, 37, quality, 4, 0, .5f);
        AssertFinite(opaque); AssertFinite(transparentOccluder); AssertFinite(partialReceiver);
        var strongest=ImageAssertions.Extremum(opaque.Texture,0,false,new(new(),33,0,0,32,37));
        int receiver=(strongest.Y*65+33+strongest.X)*4;
        Assert.True(strongest.Value<.99f,$"Expected contact occlusion, got {strongest.Value}");
        Assert.InRange(GpuLocalRead.Scalar(transparentOccluder.Texture,receiver),.995f,1.001f);
        float expected=.5f+.5f*strongest.Value;
        Assert.InRange(GpuLocalRead.Scalar(partialReceiver.Texture,receiver),expected-.002f,expected+.002f);
        // The nearer front-facing plane has no evidence in front of it, despite its darkened neighboring receiver.
        ImageAssertions.Predicate(opaque.Texture,"return bvec2(coordinate.x!=30 || (a.r>=0.995 && a.r<=1.001),false);").AssertEqual();
        using var movedAway = RenderAndCapture(owner, draw, 65, 37, quality, 0);
        ImageAssertions.Inspect(movedAway.Texture,.995f,1.001f,channelMask:1).AssertFiniteRange();
        declarations.AssertReusable(minimumTargets: 7);
    }

    /// <summary>Perpendicular ray-plane intersections form a concave corner; cutout gaps remain unoccluded.</summary>
    [Fact]
    public void PerpendicularCornerAndCutoutGapsRespectGeometry()
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        using var corner = RenderAndCapture(owner, draw, 65, 37, 2, 5);
        AssertFinite(corner);
        Assert.True(ImageAssertions.Inspect(corner.Texture,channelMask:1).Minimum<.97f,"Expected retained corner occlusion.");
        using var gaps = RenderAndCapture(owner, draw, 65, 37, 2, 6);
        AssertFinite(gaps);
        ImageAssertions.Predicate(gaps.Texture,"return bvec2(coordinate.x%4>=2 || a.r==1.0,false);").AssertEqual();
    }

    /// <summary>A narrow foreground strip releases occlusion beyond contact while a continuous foreground wall retains stronger evidence.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ThinOccluderReleasesHorizonBehindContact(int quality)
    {
        EnsureShaderTestAvailable();
        using var owner=new AmbientOcclusionPass();using var draw=new PostprocessDraw();
        const int width=257,height=129;
        using var wall=RenderAndCapture(owner,draw,width,height,quality,4);
        using var thin=RenderAndCapture(owner,draw,width,height,quality,9);
        AssertFinite(wall);AssertFinite(thin);
        // Both scenes share the same foreground edge; compare the background halo outside its immediate contact pixels.
        var area=new RHITextureRegion(new(),width/2+5,height/4,0,15,3*height/4-height/4);
        double count=area.Width*area.Height;
        double wallMean=ImageAssertions.Statistics(wall.Texture,area,channelMask:1).Sum(0)/count;
        double thinMean=ImageAssertions.Statistics(thin.Texture,area,channelMask:1).Sum(0)/count;
        log.WriteLine($"Running horizon quality{quality}: wall visibility={wallMean:R}, thin strip visibility={thinMean:R}");
        Assert.True(wallMean<.995,$"Expected retained wall evidence: {wallMean:R}");
        Assert.True(thinMean>wallMean+.0005,$"Expected thin-object release: wall={wallMean:R}, thin={thinMean:R}");
    }

    /// <summary>Reports realistic-resolution visibility near analytic corners at representative receiver distances.</summary>
    [Theory]
    [InlineData(4,3f)]
    [InlineData(4,10f)]
    [InlineData(4,20f)]
    [InlineData(5,3f)]
    [InlineData(5,10f)]
    [InlineData(5,20f)]
    public void RealisticResolutionContactStrength(int scene, float distance)
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        using var result = RenderAndCapture(owner,draw,1280,720,2,scene,receiverDistance:distance);
        AssertFinite(result);
        int center = scene == 4 ? 640 : (int)(640 + .5f / distance * 360);
        int radius = (int)MathF.Ceiling(1.25f / distance * 360);
        int left=Math.Max(0,center-radius),right=Math.Min(1280,center+radius);
        var region=new RHITextureRegion(new(),left,180,0,right-left,360);
        var values=ImageAssertions.Inspect(result.Texture,0,MathF.BitDecrement(.99f),region,channelMask:1);
        log.WriteLine($"AO scene={scene} distance={distance} 1280x720 ROIcenter={center} radius={radius}: min={values.Minimum:R} fractionBelow.99={values.InsideRange/(double)values.Components:R}");
        Assert.True(values.Minimum<.99f);
    }

    /// <summary>Odd dimensions use ceil AO targets, stable preparations reuse storage, and resize/disposal retire the publication.</summary>
    [Fact]
    public void HierarchyStorageAndPublicationFollowLifecycle()
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        Render(owner, draw, 65, 37, 1, 0);
        var first = owner.Texture;
        var masks=Assert.IsType<MinimumChildMaskPass>(HarmonyLib.AccessTools.Field(typeof(AmbientOcclusionPass),"minimumChildren").GetValue(owner));
        var firstMasks=masks.Texture;
        long expected = 8L * (2 * 33 * 19 + 65 * 37);
        for(int level=1;level<=4;level++) expected+=2L*Math.Max(1,65>>level)*Math.Max(1,37>>level);
        Assert.Equal(expected, owner.StorageBytes);
        Render(owner, draw, 65, 37, 2, 0);
        Assert.Same(first, owner.Texture);
        Assert.Same(firstMasks,masks.Texture);
        Render(owner, draw, 17, 9, 2, 0);
        Assert.NotSame(first, owner.Texture);
        Assert.NotSame(firstMasks,masks.Texture);
        // OpenGL may recycle numeric names during replacement; the retired object is the lifetime authority.
        Assert.True(firstMasks!.IsDisposed);
        Assert.Equal(17, owner.Texture!.Width); Assert.Equal(9, owner.Texture.Height);
        var resizedMasks=masks.Texture!;
        Render(owner,draw,1,1,1,0);
        Assert.True(resizedMasks.IsDisposed);
        var collapsedMasks=masks.Texture;
        Render(owner,draw,1,1,2,0);
        Assert.Same(collapsedMasks,masks.Texture);
        int collapsedMaskId=masks.Texture!.TextureId;
        owner.Dispose(); Assert.Null(owner.Texture); Assert.Equal(0, owner.StorageBytes);
        Assert.Null(masks.Texture);Assert.Equal(0,masks.StorageBytes);Assert.True(resizedMasks.IsDisposed);
        Assert.False(GL.IsTexture(collapsedMaskId));
        Assert.Equal(new AmbientOcclusionQuality(2, 3, 4), AmbientOcclusionQuality.FromNative(1));
        Assert.Equal(new AmbientOcclusionQuality(2, 6, 6), AmbientOcclusionQuality.FromNative(2));
    }
    /// <summary>Measures only warmed GPU submissions on a synthetic contact scene, separately from allocation and readback.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [Trait("Purpose","Diagnostic")]
    public void SyntheticPassTiming(int quality)
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        using var image=RenderAndCapture(owner,draw,1280,720,quality,4,measure:true);AssertFinite(image);
    }
    #endregion

    #region Private
    /// <summary>Builds depth from analytic view-space planes and stages material layers through existing typed textures.</summary>
    private void Render(AmbientOcclusionPass owner, PostprocessDraw draw, int width, int height, int quality, int scene,
        float occluderTransmission = 0, float receiverTransmission = 0, bool measure = false, float receiverDistance = 3)
    {
        using var inputs=new AmbientOcclusionSceneFixture(TestFramework,Programs,width,height,scene,
            receiverDistance:receiverDistance,occluderTransmission:occluderTransmission,receiverTransmission:receiverTransmission);
        inputs.Render(owner,draw,quality);
        if(measure) {
            var samples=inputs.Measure(owner,draw,quality).Gpu;
            log.WriteLine($"Synthetic AO {width}x{height} quality{quality}, hierarchy plus up to four mask draws + three AO draws, five warm GPU samples: min={samples.Min():F4}ms median={samples.Order().ElementAt(2):F4}ms max={samples.Max():F4}ms; AO payload={owner.StorageBytes} bytes, shared hierarchy={inputs.HierarchyBytes} bytes. Not live cost or physical bandwidth.");
        }

    }

    /// <summary>Renders the unchanged scene and retains an independent image only when a consumer needs an observation.</summary>
    private GpuImageSnapshot RenderAndCapture(AmbientOcclusionPass owner,PostprocessDraw draw,int width,int height,int quality,int scene,
        float occluderTransmission=0,float receiverTransmission=0,bool measure=false,float receiverDistance=3)
    {
        Render(owner,draw,width,height,quality,scene,occluderTransmission,receiverTransmission,measure,receiverDistance);
        return new GpuImageSnapshot((DynamicTexture2D)owner.Texture!);
    }

    /// <summary>Rejects nonfinite and out-of-range visibility while allowing depth and packed normal channels.</summary>
    private void AssertFinite(GpuImageSnapshot image)
    {
        ImageAssertions.Inspect(image.Texture).AssertFiniteRange();
        ImageAssertions.Inspect(image.Texture,0,1.001f,channelMask:1).AssertFiniteRange();
    }
    #endregion
}
