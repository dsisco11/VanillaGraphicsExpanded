using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Checks the production bloom shader and multiscale graph against independent energy and spatial expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class OwnedPostprocessShaderTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{


    private PostprocessDraw? draw;

    #region Public API
    /// <summary>Selection uses exposed luminance and preserves selected scene radiance and chromaticity.</summary>
    [Theory]
    [InlineData(0f, false, 0f)]
    [InlineData(1f, false, .5f)]
    [InlineData(2f, false, 1f)]
    [InlineData(1f, true, .5f)]
    public void BloomExposureSelectsWithoutScalingRadiance(float ev, bool automatic, float weight)
    {
        // Arrange: Select EV, camera mode, and threshold weight over a fixed colored radiance input.
        EnsureShaderTestAvailable();
        var shader = Programs.Create<BloomShaderProgram>();
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [1f,1f,1f,.3f]);
        using var exposure = ExposureTestStorage.Create(ev);
        using var target = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        shader.SourceImage=source; shader.SecondaryImage=source; shader.ExposureBuffer=exposure;
        // Act and assert: Draw at each selected EV/mode and verify threshold weight without changing source radiance.
        shader.CaptureExtraction(true, automatic, 2, .5f, 1, automatic ? -8 : ev); Draw(shader,target);
        Assert.InRange(target[0].ReadTexel()[0],weight-.00001f,weight+.00001f);
        Assert.Equal(new[]{1f,1f,1f,.3f},source.ReadPixels());
    }

    /// <summary>Colored highlights are selected by luminance, with continuous knees and an explicit threshold bypass.</summary>
    [Fact]
    public void BloomLuminanceThresholdAndBypassAreDefined()
    {
        // Arrange: Supply colored samples around the luminance threshold and a bypass configuration.
        EnsureShaderTestAvailable();
        var shader=Programs.Create<BloomShaderProgram>();
        using var red=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[4f,0f,0f,1f]);
        using var green=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[0f,4f,0f,1f]);
        using var target=TestFramework.CreateTestGBuffer(1,1,TextureFormat.Rgba32f);
        // Act and assert: Render colored threshold cases, bypass, and three knee positions through the production shader.
        shader.SecondaryImage=red;
        shader.SourceImage=red; shader.CaptureExtraction(true, false, 1, 0, 1, 0); Draw(shader,target);
        Assert.Equal(0,target[0].ReadTexel()[0]);
        shader.SourceImage=green; Draw(shader,target);
        Assert.Equal(4,Assertions.GpuLocalRead.Scalar(target[0], 1));
        shader.SourceImage=red; shader.CaptureExtraction(true, false, 0, .5f, .25f, -20); Draw(shader,target);
        Assert.Equal(new[]{1f,0f,0f,1f},target[0].ReadPixels());
        using var knee=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[1f,1f,1f,1f]);
        shader.SourceImage=knee;
        foreach(float threshold in new[]{.999f,1f,1.001f})
        {
            shader.CaptureExtraction(true, false, threshold, .5f, 1, 0); Draw(shader,target);
            float t=Math.Clamp((1-threshold*.5f)/threshold,0,1);
            float expected=t*t*(3-2*t);
            Assert.InRange(target[0].ReadTexel()[0],expected-.00001f,expected+.00001f);
        }
    }

    /// <summary>Each symmetric source sample is selected before reduction so sparse HDR highlights survive averaging.</summary>
    [Fact]
    public void BloomExtractionSelectsBeforeSymmetricReduction()
    {
        // Arrange: Place one HDR highlight beside three black samples so threshold ordering is visible after reduction.
        EnsureShaderTestAvailable();var shader=Programs.Create<BloomShaderProgram>();
        using var source=TestFramework.CreateTexture(2,2,TextureFormat.Rgba32f,[4f,4f,4f,1f,0f,0f,0f,1f,0f,0f,0f,1f,0f,0f,0f,1f]);
        using var target=TestFramework.CreateTestGBuffer(1,1,TextureFormat.Rgba32f);
        shader.SourceImage=source;shader.SecondaryImage=source;
        // Act and assert: Select each source before reduction and require the lone highlight to survive the 2x2 average.
        shader.CaptureExtraction(true, false, 2, 0, 1, 0);Draw(shader,target);
        Assert.Equal(new[]{1f,1f,1f,1f},target[0].ReadPixels());
    }

    /// <summary>Every normalized Gaussian axis and additive scale reconstruction retains constant HDR energy.</summary>
    [Fact]
    public void BloomThresholdAndConstantEnergyAreDefined()
    {
        // Arrange: Prepare constant HDR input and a normalized multiscale Gaussian configuration.
        EnsureShaderTestAvailable();
        var shader=Programs.Create<BloomShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[4f,2f,1f,.3f]);
        using var secondary=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[3f,1.5f,.75f,1f]);
        using var target=TestFramework.CreateTestGBuffer(17,9,TextureFormat.Rgba32f);
        shader.SourceImage=source; shader.SecondaryImage=secondary;
        // Act and assert: Exercise threshold, bypass, Gaussian, and additive reconstruction modes on constant inputs.
        foreach(int mode in new[]{1,2,3,4})
        {
            if(mode is 2 or 3)shader.CaptureGaussian(4,mode==3);
            else if (mode == 4) shader.CaptureAccumulation(new(.25f), true);
            else shader.CaptureExtraction(false, false, 0, 0, 0, 0);
            Draw(shader,target);
            ImageAssertions.Predicates.Predicate(target[0], LightingPredicates.OwnedPostprocessShaderTests_BloomThresholdAndConstantEnergyAreDefined).AssertEqual();
        }
    }

    /// <summary>The normalized Gaussian forms a symmetric HDR halo independent of viewport aspect.</summary>
    [Fact]
    public void BloomPyramidProducesSpatialHaloOnNonSquareViewport()
    {
        // Arrange: Place a point impulse in a non-square source and prepare the production bloom pyramid.
        EnsureShaderTestAvailable();
        var shader=Programs.Create<BloomShaderProgram>();
        float[] pixels=new float[33*17*4];
        pixels[(8*33+16)*4]=256;
        using var source=TestFramework.CreateTexture(33,17,TextureFormat.Rgba32f,pixels);
        using var horizontal=TestFramework.CreateTestGBuffer(33,17,TextureFormat.Rgba32f);
        using var result=TestFramework.CreateTestGBuffer(33,17,TextureFormat.Rgba32f);
        shader.SourceImage=source; shader.SecondaryImage=source;
        shader.CaptureGaussian(4,false); Draw(shader,horizontal);
        // Act: Apply the horizontal and vertical production Gaussian passes to the non-square impulse.
        shader.SourceImage=horizontal[0]; shader.CaptureGaussian(4,true); Draw(shader,result);
        var lower=new double[33*17];var upper=new double[lower.Length];
        float normalization=Enumerable.Range(-4,9).Sum(t=>MathF.Exp(-16.7f*t*t/16));
        float[] coefficients=Enumerable.Range(0,5).Select(t=>MathF.Exp(-16.7f*t*t/16)/normalization).ToArray();

        for(int y=0;y<17;y++) for(int x=0;x<33;x++)
        {
            int dx=x-16,dy=y-8;
            float expected=Math.Abs(dx)<=4&&Math.Abs(dy)<=4 ? 256*MathF.Exp(-16.7f*(dx*dx+dy*dy)/16)/(normalization*normalization):0;

            float wx=Math.Abs(dx)<=4?coefficients[Math.Abs(dx)]:0,wy=Math.Abs(dy)<=4?coefficients[Math.Abs(dy)]:0;
            float ex=GaussianInterpolationError(coefficients,dx),ey=GaussianInterpolationError(coefficients,dy);
            float tolerance=256*(wx*ey+wy*ex+ex*ey)+.00001f;
            int scalar=y*33+x;lower[scalar]=expected-tolerance;upper[scalar]=expected+tolerance;
        }
        // Assert: The full halo matches the independent kernel bounds, conserves energy, and remains spatially supported.
        ImageAssertions.Comparisons.CompareComponentIntervals(result[0],lower,upper,1).AssertEqual();
        var mass=ImageAssertions.Statistics.Statistics(result[0],channelMask:1);mass.AssertFiniteRange();
        Assert.InRange(mass.Sum(0),255.99,256.01);
        Assert.True(GpuLocalRead.Scalar(result[0],(8*33+16)*4)>1);
        Assert.True(GpuLocalRead.Scalar(result[0],(6*33+14)*4)>0);
    }

    /// <summary>Every configured support radius must cover intervening texels without comb-shaped troughs as an impulse moves.</summary>
    [Theory]
    [InlineData(3,0f)]
    [InlineData(3,0.25f)]
    [InlineData(3,0.5f)]
    [InlineData(3,0.75f)]
    [InlineData(4,0f)]
    [InlineData(4,0.25f)]
    [InlineData(4,0.5f)]
    [InlineData(4,0.75f)]
    [InlineData(5,0f)]
    [InlineData(5,0.25f)]
    [InlineData(5,0.5f)]
    [InlineData(5,0.75f)]
    [InlineData(6,0f)]
    [InlineData(6,0.25f)]
    [InlineData(6,0.5f)]
    [InlineData(6,0.75f)]
    [InlineData(7,0f)]
    [InlineData(7,0.25f)]
    [InlineData(7,0.5f)]
    [InlineData(7,0.75f)]
    [InlineData(8,0f)]
    [InlineData(8,0.25f)]
    [InlineData(8,0.5f)]
    [InlineData(8,0.75f)]
    public void BloomGaussianFootprintHasNoSparseTapTroughs(int radius,float phase)
    {
        // Arrange: Select an authored support radius and subpixel impulse phase that can reveal tap gaps.
        EnsureShaderTestAvailable();var shader=Programs.Create<BloomShaderProgram>();
        using var target=TestFramework.CreateTestGBuffer(33,17,TextureFormat.Rgba32f);
        {
            float[] pixels=new float[33*17*4];
            pixels[(8*33+16)*4]=1-phase;pixels[(8*33+17)*4]=phase;
            using var source=TestFramework.CreateTexture(33,17,TextureFormat.Rgba32f,pixels);
            shader.SourceImage=source;shader.SecondaryImage=source;
            // Act and assert: Render each subpixel phase, compare every row sample to the independent Gaussian, and check monotonic support.
            shader.CaptureGaussian(radius,false);Draw(shader,target);
            var lower=new double[33];var upper=new double[lower.Length];
            float[] kernel=Enumerable.Range(-radius,2*radius+1).Select(x=>MathF.Exp(-16.7f*x*x/(radius*radius))).ToArray();
            float normalization=kernel.Sum();
            float[] coefficients=kernel.Skip(radius).Select(v=>v/normalization).ToArray();

            for(int x=0;x<33;x++)
            {
                int first=x-16,second=x-17;
                float expected=(Math.Abs(first)<=radius ? (1-phase)*kernel[first+radius]/normalization:0)
                    +(Math.Abs(second)<=radius ? phase*kernel[second+radius]/normalization:0);

                float tolerance=(1-phase)*GaussianInterpolationError(coefficients,first)
                    +phase*GaussianInterpolationError(coefficients,second)+.00001f;
                lower[x]=expected-tolerance;upper[x]=expected+tolerance;
            }
            var row=new RHITextureRegion(new(0),0,8,0,33,1,1);
            ImageAssertions.Comparisons.CompareComponentIntervals(target[0],lower,upper,1,row).AssertEqual();
            var massProof=ImageAssertions.Statistics.Statistics(target[0],row,1);massProof.AssertFiniteRange();
            var transform = ImageAssertions.Projections;
            using var weighted=transform.Apply(target[0], LightingProjections.OwnedPostprocessShaderTests_BloomGaussianFootprintHasNoSparseTapTroughs);
            var momentProof=ImageAssertions.Statistics.Statistics(weighted,row,1);momentProof.AssertFiniteRange();
            double mass=massProof.Sum(0),moment=momentProof.Sum(0);
            Assert.InRange(mass,.9999,1.0001);Assert.InRange(moment/mass,16+phase-.0001,16+phase+.0001);
            if(phase==0)
            {
                // The pure Gaussian truncation ratio is radius-independent. Tiny paired tails may round to zero.
                float expectedRatio=MathF.Exp(-16.7f);
                float actualRatio=GpuLocalRead.Scalar(target[0],(8*33+16+radius)*4)/GpuLocalRead.Scalar(target[0],(8*33+16)*4);
                float ratioTolerance=GaussianInterpolationError(coefficients,radius)/coefficients[0]+1e-10f;
                Assert.InRange(actualRatio,Math.Max(0,expectedRatio-ratioTolerance),expectedRatio+ratioTolerance);
                output.WriteLine($"Gaussian radius={radius} boundary/center={actualRatio:G9}, ideal={expectedRatio:G9}, absolute bound={ratioTolerance:G9}");
            }
            var monotone=ImageAssertions.Predicates.Predicate(target[0], LightingPredicates.OwnedPostprocessShaderTests_BloomGaussianFootprintHasNoSparseTapTroughs_2,parameters:new(phase==0?0:1,0,0,0));
            monotone.AssertEqual();Assert.Equal(phase==0?10u:9u,monotone.MatchingTexels);
        }
    }

    /// <summary>The independent Gaussian monotonicity oracle selects only its authored row/columns and exposes a nominated defect.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void GaussianWitnessMaskRetainsAuthoredRowAndColumns(int phase)
    {
        // Arrange: Build an independent witness mask for the nominated row and columns at this impulse phase.
        EnsureShaderTestAvailable();
        var pixels = new float[33*17*4];
        using var source = TestFramework.CreateTexture(33,17,TextureFormat.Rgba32f,pixels);
        var predicate = LightingPredicates.OwnedPostprocessShaderTests_BloomGaussianFootprintHasNoSparseTapTroughs_2;
        // Act and assert: Apply the oracle to an empty image and then introduce one nominated defect to verify its witness.
        var result = ImageAssertions.Predicates.Predicate(source,predicate,parameters:new(phase,0,0,0));
        result.AssertEqual(); Assert.Equal((uint)(10-phase),result.MatchingTexels);
        Assert.Equal(8*33+17+phase,result.FirstWitnessIndex);
        pixels[(8*33+23)*4]=1;
        source.UploadDataImmediate(pixels);
        var failure = ImageAssertions.Predicates.Predicate(source,predicate,parameters:new(phase,0,0,0));
        Assert.Equal(1u,failure.Mismatches); Assert.Equal((uint)(10-phase),failure.MatchingTexels);
        Assert.Contains("(23,8,0)",failure.ToString());
    }

    /// <summary>The production graph normalizes every selected scale and replaces retired images on resize or explicit teardown.</summary>
    [Theory]
    [InlineData(3,129,65)]
    [InlineData(4,129,65)]
    [InlineData(5,129,65)]
    [InlineData(6,129,65)]
    [InlineData(6,1,1)]
    public void BloomOwnerPreservesConstantGainAndLifetime(int levels,int width,int height)
    {
        // Arrange: Select pyramid depth and odd/even extent while observing the owner's declared passes and resources.
        EnsureShaderTestAvailable();
        using var declarations = new StaticPassDeclarationCapture();
        using var assets=new BinaryShaderApiFixture();
        using var platform=new EngineShaderPlatformScope();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        using var source=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[16f,8f,4f,.4f]);
        using var publication=new TestFrameLightingPublication();
        GpuShaderProgramLibraries.Get<BloomShaderProgram>(assets.Api, "pbr_bloom")!.FrameLightingInputs=publication.Buffer;
        using var owner=new BloomRenderer();
        using var submission=new PostprocessDraw();
        var settings=new PostprocessParameters(.125f,0,.5f,levels,0,1,16);
        var pipeline=owner.Prepare(assets.Api,submission,width,height,levels);
        // Act and assert: Render, reuse, resize, and dispose the graph while checking normalized gain and image retirement.
        var first=owner.Texture;
        {
    var commands=RHICommandContext.Current;
    using var scope64=commands.EnterBoundary(new("Tests.BloomOwner",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope64.Run(()=>owner.Render(commands,submission,source,(null,-20),settings));
}
        ImageAssertions.Predicates.Predicate(first, LightingPredicates.OwnedPostprocessShaderTests_BloomOwnerPreservesConstantGainAndLifetime).AssertEqual();
        owner.Prepare(assets.Api,submission,width,height,levels);
        Assert.Same(first,owner.Texture);
        {
    var commands=RHICommandContext.Current;
    using var scope65=commands.EnterBoundary(new("Tests.BloomOwner.Reused",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope65.Run(()=>owner.Render(commands,submission,source,(null,-20),settings));
}
        declarations.AssertReusable();
        owner.Prepare(assets.Api,submission,width+2,height+2,levels);
        Assert.NotSame(first,owner.Texture);Assert.Equal(0,first.TextureId);
        var resized=owner.Texture;
        owner.Dispose();Assert.Equal(0,resized.TextureId);
        owner.Prepare(assets.Api,submission,width,height,levels);
        Assert.NotSame(resized,owner.Texture);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>The actual odd-sized graph spreads a source impulse symmetrically and adds broader support with more scales.</summary>
    [Fact]
    public void BloomOwnerReconstructsSymmetricMultiscaleImpulse()
    {
        // Arrange: Prepare the packaged production graph and a centered impulse on an odd-sized viewport.
        EnsureShaderTestAvailable();
        using var assets=new BinaryShaderApiFixture();
        using var platform=new EngineShaderPlatformScope();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        float[] pixels=new float[257*129*4];pixels[(64*257+128)*4]=256;
        using var source=TestFramework.CreateTexture(257,129,TextureFormat.Rgba32f,pixels);
        using var publication=new TestFrameLightingPublication();
        GpuShaderProgramLibraries.Get<BloomShaderProgram>(assets.Api, "pbr_bloom")!.FrameLightingInputs=publication.Buffer;
        using var owner=new BloomRenderer();
        using var submission=new PostprocessDraw();
        int narrowSupport=0;
        foreach(int levels in new[]{3,6})
        {
            // Act and assert: Render the same impulse at narrow and broad graph depths and compare symmetry/support.
            var settings=new PostprocessParameters(1,0,0,levels,0,1,16);
            var pipeline=owner.Prepare(assets.Api,submission,257,129,levels);
            {
    var commands=RHICommandContext.Current;
    using var scope66=commands.EnterBoundary(new("Tests.BloomImpulse",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope66.Run(()=>owner.Render(commands,submission,source,(null,0),settings));
}
            Assert.Equal(129,owner.Texture.Width);Assert.Equal(65,owner.Texture.Height);
            var halo=ImageAssertions.Predicates.Predicate(owner.Texture, LightingPredicates.OwnedPostprocessShaderTests_BloomOwnerReconstructsSymmetricMultiscaleImpulse);
            halo.AssertEqual();int support=checked((int)halo.MatchingTexels);
            if(levels==3)narrowSupport=support;else Assert.True(support>narrowSupport);
        }
        ImageAssertions.Comparisons.Compare(source,pixels).AssertEqual();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Measures the complete owned graph with fixed synthetic inputs, excluding allocation and shader compilation.</summary>
    [Fact]
    [Trait("Purpose","Diagnostic")]
    public void SyntheticBloomGraphTiming()
    {
        // Arrange: Prepare fixed synthetic bloom input and the fully owned graph before timing warmed submissions.
        EnsureShaderTestAvailable();
        using var assets=new BinaryShaderApiFixture();
        using var platform=new EngineShaderPlatformScope();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        using var source=TestFramework.CreateTexture(1280,720,TextureFormat.Rgba16f,CreateUniformColorData(1280,720,8,4,2));
        using var publication=new TestFrameLightingPublication();
        GpuShaderProgramLibraries.Get<BloomShaderProgram>(assets.Api, "pbr_bloom")!.FrameLightingInputs=publication.Buffer;
        using var owner=new BloomRenderer();
        using var submission=new PostprocessDraw();
        var settings=new PostprocessSettings().Snapshot();
        var pipeline=owner.Prepare(assets.Api,submission,1280,720,settings.BloomLevels);
        // Act and report: Warm the complete owned graph and measure five GPU submissions after setup is excluded.
        var timings=new List<double>();
        for(int sample=-2;sample<5;sample++)
        {
            using var timer=GpuTimerQuery.Create();timer.Begin();
            {
    var commands=RHICommandContext.Current;
    using var scope67=commands.EnterBoundary(new("Tests.BloomTiming",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope67.Run(()=>owner.Render(commands,submission,source,(null,0),settings));
}
            timer.End();double elapsed=timer.GetResultNanoseconds()/1e6;
            if(sample>=0)timings.Add(elapsed);
        }
        // Report warmed GPU timings for the fixed graph; this diagnostic makes no performance assertion.
        output.WriteLine($"Synthetic owned bloom 1280x720, five scales, 20 draws, constant HDR source: min={timings.Min():F4}ms median={timings.Order().ElementAt(2):F4}ms max={timings.Max():F4}ms. Not live GPU cost or physical bandwidth.");
    }

    #endregion

    #region Protected API
    /// <summary>Retires the retained graphics pipeline and geometry before the test shader owners.</summary>
    protected override void Dispose(bool disposing)
    {
        if(disposing){draw?.Dispose();draw=null;}
        base.Dispose(disposing);
    }

    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-lighting", 0, 0);
        programs.Prepare("projection-lighting", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }
    #endregion

    #region Private
    /// <summary>Bounds ideal-kernel error from a half-step of measured 1/256 bilinear interpolation precision.</summary>
    private static float GaussianInterpolationError(float[] coefficients,int distance)
    {
        distance=Math.Abs(distance);
        if(distance==0||distance>=coefficients.Length)return 0;
        int first=((distance-1)/2)*2+1;
        if(first+1>=coefficients.Length)return 0; // An unpaired integer texel needs no interpolation.
        return (coefficients[first]+coefficients[first+1])/512;
    }

    /// <summary>Submits the production procedural triangle with deterministic raster state after any readback.</summary>
    private void Draw(GpuProgram program,GpuFramebuffer target)
    {
        draw??=new PostprocessDraw();
        var pipeline=draw.Prepare(program,target.PostprocessPass());
        {
    var commands=RHICommandContext.Current;
    using var scope68=commands.EnterBoundary(new("Tests.Postprocess",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope68.Run(()=>draw.Submit(commands,pipeline,target.PostprocessPass(), program.CaptureBindings()));
}
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
