using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the production bloom shader and multiscale graph against independent energy and spatial expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
        EnsureShaderTestAvailable();
        var shader = Programs.Create<BloomShaderProgram>();
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [1f,1f,1f,.3f]);
        using var exposure = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [ev,0f,0f,1f]);
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        shader.SourceImage=source; shader.SecondaryImage=exposure;
        shader.Capture(new(0,0,0,automatic?1:0),new(2,.5f,1,automatic?-8:ev)); Draw(shader,target);
        Assert.InRange(target[0].ReadPixels()[0],weight-.00001f,weight+.00001f);
        Assert.Equal(new[]{1f,1f,1f,.3f},source.ReadPixels());
    }

    /// <summary>Colored highlights are selected by luminance, with continuous knees and an explicit threshold bypass.</summary>
    [Fact]
    public void BloomLuminanceThresholdAndBypassAreDefined()
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<BloomShaderProgram>();
        using var red=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[4f,0f,0f,1f]);
        using var green=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[0f,4f,0f,1f]);
        using var target=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        shader.SecondaryImage=red;
        shader.SourceImage=red; shader.Capture(Vector4.Zero,new(1,0,1,0)); Draw(shader,target);
        Assert.Equal(0,target[0].ReadPixels()[0]);
        shader.SourceImage=green; Draw(shader,target);
        Assert.Equal(4,target[0].ReadPixels()[1]);
        shader.SourceImage=red; shader.Capture(Vector4.Zero,new(0,.5f,.25f,-20)); Draw(shader,target);
        Assert.Equal(new[]{1f,0f,0f,1f},target[0].ReadPixels());
        using var knee=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[1f,1f,1f,1f]);
        shader.SourceImage=knee;
        foreach(float threshold in new[]{.999f,1f,1.001f})
        {
            shader.Capture(Vector4.Zero,new(threshold,.5f,1,0)); Draw(shader,target);
            float t=Math.Clamp((1-threshold*.5f)/threshold,0,1);
            float expected=t*t*(3-2*t);
            Assert.InRange(target[0].ReadPixels()[0],expected-.00001f,expected+.00001f);
        }
    }

    /// <summary>Each symmetric source sample is selected before reduction so sparse HDR highlights survive averaging.</summary>
    [Fact]
    public void BloomExtractionSelectsBeforeSymmetricReduction()
    {
        EnsureShaderTestAvailable();var shader=Programs.Create<BloomShaderProgram>();
        using var source=TestFramework.CreateTexture(2,2,PixelInternalFormat.Rgba32f,[4f,4f,4f,1f,0f,0f,0f,1f,0f,0f,0f,1f,0f,0f,0f,1f]);
        using var target=TestFramework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        shader.SourceImage=source;shader.SecondaryImage=source;
        shader.Capture(Vector4.Zero,new(2,0,1,0));Draw(shader,target);
        Assert.Equal(new[]{1f,1f,1f,1f},target[0].ReadPixels());
    }

    /// <summary>Every normalized Gaussian axis and additive scale reconstruction retains constant HDR energy.</summary>
    [Fact]
    public void BloomThresholdAndConstantEnergyAreDefined()
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<BloomShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[4f,2f,1f,.3f]);
        using var secondary=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[3f,1.5f,.75f,1f]);
        using var target=TestFramework.CreateTestGBuffer(17,9,PixelInternalFormat.Rgba32f);
        shader.SourceImage=source; shader.SecondaryImage=secondary;
        foreach(int mode in new[]{1,2,3,4})
        {
            if(mode is 2 or 3)shader.CaptureGaussian(4,mode==3);
            else shader.Capture(new(4,0,mode,mode==4?1:0),mode==4?new(.25f,.25f,.25f,0):Vector4.Zero);
            Draw(shader,target);
            float[] result=target[0].ReadPixels();
            for(int i=0;i<result.Length;i+=4)
            {
                Assert.InRange(result[i],3.99999f,4.00001f);
                Assert.InRange(result[i+1],1.99999f,2.00001f);
                Assert.InRange(result[i+2],.99999f,1.00001f);
            }
        }
    }

    /// <summary>The normalized Gaussian forms a symmetric HDR halo independent of viewport aspect.</summary>
    [Fact]
    public void BloomPyramidProducesSpatialHaloOnNonSquareViewport()
    {
        EnsureShaderTestAvailable();
        var shader=Programs.Create<BloomShaderProgram>();
        float[] pixels=new float[33*17*4];
        pixels[(8*33+16)*4]=256;
        using var source=TestFramework.CreateTexture(33,17,PixelInternalFormat.Rgba32f,pixels);
        using var horizontal=TestFramework.CreateTestGBuffer(33,17,PixelInternalFormat.Rgba32f);
        using var result=TestFramework.CreateTestGBuffer(33,17,PixelInternalFormat.Rgba32f);
        shader.SourceImage=source; shader.SecondaryImage=source;
        shader.CaptureGaussian(4,false); Draw(shader,horizontal);
        shader.SourceImage=horizontal[0]; shader.CaptureGaussian(4,true); Draw(shader,result);
        float[] broad=result[0].ReadPixels();
        float normalization=Enumerable.Range(-4,9).Sum(t=>MathF.Exp(-16.7f*t*t/16));
        float[] coefficients=Enumerable.Range(0,5).Select(t=>MathF.Exp(-16.7f*t*t/16)/normalization).ToArray();
        double sum=0;
        for(int y=0;y<17;y++) for(int x=0;x<33;x++)
        {
            int dx=x-16,dy=y-8;
            float expected=Math.Abs(dx)<=4&&Math.Abs(dy)<=4 ? 256*MathF.Exp(-16.7f*(dx*dx+dy*dy)/16)/(normalization*normalization):0;
            float value=broad[(y*33+x)*4];
            float wx=Math.Abs(dx)<=4?coefficients[Math.Abs(dx)]:0,wy=Math.Abs(dy)<=4?coefficients[Math.Abs(dy)]:0;
            float ex=GaussianInterpolationError(coefficients,dx),ey=GaussianInterpolationError(coefficients,dy);
            float tolerance=256*(wx*ey+wy*ex+ex*ey)+.00001f;
            Assert.InRange(value,expected-tolerance,expected+tolerance);
            sum+=value;
        }
        Assert.InRange(sum,255.99,256.01);
        Assert.True(broad[(8*33+16)*4]>1);
        Assert.True(broad[(6*33+14)*4]>0);
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
        EnsureShaderTestAvailable();var shader=Programs.Create<BloomShaderProgram>();
        using var target=TestFramework.CreateTestGBuffer(33,17,PixelInternalFormat.Rgba32f);
        {
            float[] pixels=new float[33*17*4];
            pixels[(8*33+16)*4]=1-phase;pixels[(8*33+17)*4]=phase;
            using var source=TestFramework.CreateTexture(33,17,PixelInternalFormat.Rgba32f,pixels);
            shader.SourceImage=source;shader.SecondaryImage=source;
            shader.CaptureGaussian(radius,false);Draw(shader,target);
            float[] image=target[0].ReadPixels();
            float[] profile=Enumerable.Range(16,11).Select(x=>image[(8*33+x)*4]).ToArray();
            output.WriteLine($"Gaussian radius={radius} phase={phase}: {string.Join(", ",profile.Select(v=>v.ToString("G7")))}");
            float[] kernel=Enumerable.Range(-radius,2*radius+1).Select(x=>MathF.Exp(-16.7f*x*x/(radius*radius))).ToArray();
            float normalization=kernel.Sum();
            float[] coefficients=kernel.Skip(radius).Select(v=>v/normalization).ToArray();
            double mass=0,moment=0;
            for(int x=0;x<33;x++)
            {
                int first=x-16,second=x-17;
                float expected=(Math.Abs(first)<=radius ? (1-phase)*kernel[first+radius]/normalization:0)
                    +(Math.Abs(second)<=radius ? phase*kernel[second+radius]/normalization:0);
                float actual=image[(8*33+x)*4];
                float tolerance=(1-phase)*GaussianInterpolationError(coefficients,first)
                    +phase*GaussianInterpolationError(coefficients,second)+.00001f;
                Assert.InRange(actual,expected-tolerance,expected+tolerance);
                mass+=actual;moment+=actual*x;
            }
            Assert.InRange(mass,.9999,1.0001);Assert.InRange(moment/mass,16+phase-.0001,16+phase+.0001);
            if(phase==0)
            {
                // The pure Gaussian truncation ratio is radius-independent. Tiny paired tails may round to zero.
                float expectedRatio=MathF.Exp(-16.7f);
                float actualRatio=profile[radius]/profile[0];
                float ratioTolerance=GaussianInterpolationError(coefficients,radius)/coefficients[0]+1e-10f;
                Assert.InRange(actualRatio,Math.Max(0,expectedRatio-ratioTolerance),expectedRatio+ratioTolerance);
                output.WriteLine($"Gaussian radius={radius} boundary/center={actualRatio:G9}, ideal={expectedRatio:G9}, absolute bound={ratioTolerance:G9}");
            }
            int start=phase==0?0:1;
            for(int x=start+1;x<profile.Length;x++)
                Assert.True(profile[x]<=profile[x-1]+.00001f,
                    $"Gaussian radius={radius}, phase={phase} has a trough at offset {x-1}: {profile[x-1]:G7} followed by {profile[x]:G7}. Profile: {string.Join(", ",profile)}");
        }
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
        EnsureShaderTestAvailable();
        using var assets=new BinaryShaderApiFixture();
        using var platform=new EngineShaderPlatformScope();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        using var source=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[16f,8f,4f,.4f]);
        using var owner=new BloomRenderer();
        using var submission=new PostprocessDraw();
        var settings=new PostprocessParameters(.125f,0,.5f,levels,0,1,16);
        var pipeline=owner.Prepare(assets.Api,submission,width,height,levels);
        var first=owner.Texture;
        Assert.True(GraphicsCommandContext.TryRun("Tests.BloomOwner",[pipeline],true,commands=>owner.Render(commands,submission,source,(null,-20),settings)));
        float[] actual=first.ReadPixels();
        for(int pixel=0;pixel<actual.Length;pixel+=4)
        {
            Assert.InRange(actual[pixel],1.99f,2.01f);
            Assert.InRange(actual[pixel+1],.995f,1.005f);
            Assert.InRange(actual[pixel+2],.4975f,.5025f);
        }
        owner.Prepare(assets.Api,submission,width,height,levels);
        Assert.Same(first,owner.Texture);
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
        EnsureShaderTestAvailable();
        using var assets=new BinaryShaderApiFixture();
        using var platform=new EngineShaderPlatformScope();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        float[] pixels=new float[257*129*4];pixels[(64*257+128)*4]=256;
        using var source=TestFramework.CreateTexture(257,129,PixelInternalFormat.Rgba32f,pixels);
        using var owner=new BloomRenderer();
        using var submission=new PostprocessDraw();
        int narrowSupport=0;
        foreach(int levels in new[]{3,6})
        {
            var settings=new PostprocessParameters(1,0,0,levels,0,1,16);
            var pipeline=owner.Prepare(assets.Api,submission,257,129,levels);
            Assert.True(GraphicsCommandContext.TryRun("Tests.BloomImpulse",[pipeline],true,commands=>owner.Render(commands,submission,source,(null,0),settings)));
            float[] halo=owner.Texture.ReadPixels();int support=0;
            for(int y=0;y<65;y++)for(int x=0;x<129;x++)
            {
                float value=halo[(y*129+x)*4];
                Assert.True(float.IsFinite(value)&&value>=0);
                if(value>.00001f)support++;
                Assert.InRange(halo[(y*129+128-x)*4],value-.0002f,value+.0002f);
                Assert.InRange(halo[((64-y)*129+x)*4],value-.0002f,value+.0002f);
            }
            float[] profile=Enumerable.Range(64,65).Select(x=>halo[(32*129+x)*4]).ToArray();
            float maxRise=profile.Zip(profile.Skip(1),(a,b)=>b-a).Max();
            output.WriteLine($"Owned graph levels={levels}, centered impulse: maximum outward rise={maxRise:G7}; profile={string.Join(", ",profile.Select(v=>v.ToString("G7")))}");
            if(levels==3)narrowSupport=support;else Assert.True(support>narrowSupport);
        }
        Assert.Equal(pixels,source.ReadPixels());
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Measures the complete owned graph with fixed synthetic inputs, excluding allocation and shader compilation.</summary>
    [Fact]
    public void SyntheticBloomGraphTiming()
    {
        EnsureShaderTestAvailable();
        using var assets=new BinaryShaderApiFixture();
        using var platform=new EngineShaderPlatformScope();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        using var source=TestFramework.CreateTexture(1280,720,PixelInternalFormat.Rgba16f,CreateUniformColorData(1280,720,8,4,2));
        using var owner=new BloomRenderer();
        using var submission=new PostprocessDraw();
        var settings=new PostprocessSettings().Snapshot();
        var pipeline=owner.Prepare(assets.Api,submission,1280,720,settings.BloomLevels);
        var timings=new List<double>();
        for(int sample=-2;sample<5;sample++)
        {
            using var timer=GpuTimerQuery.Create();timer.Begin();
            Assert.True(GraphicsCommandContext.TryRun("Tests.BloomTiming",[pipeline],true,commands=>owner.Render(commands,submission,source,(null,0),settings)));
            timer.End();double elapsed=timer.GetResultNanoseconds()/1e6;
            if(sample>=0)timings.Add(elapsed);
        }
        output.WriteLine($"Synthetic owned bloom 1280x720, five scales, 20 draws, constant HDR source: min={timings.Min():F4}ms median={timings.Order().ElementAt(2):F4}ms max={timings.Max():F4}ms. Not live GPU cost or physical bandwidth.");
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
        var pipeline=draw.Prepare(program,target);
        Assert.True(GraphicsCommandContext.TryRun("Tests.Postprocess",[pipeline],true,
            commands=>draw.Submit(commands,pipeline,target)));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Protected API
    /// <summary>Retires the retained graphics pipeline and geometry before the test shader owners.</summary>
    protected override void Dispose(bool disposing)
    {
        if(disposing){draw?.Dispose();draw=null;}
        base.Dispose(disposing);
    }
    #endregion
}
