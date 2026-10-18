using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks finite-budget shader arithmetic, physical units, clipping and native OIT products.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CloudVolumeDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Checks uniform-span attenuation and the declared finite-step native bucket products.</summary>
    [Theory]
    [InlineData(1000,.2f)][InlineData(5000,.05f)]
    public void NativeWideConstantFieldUsesFiniteStepOitArithmetic(float distance,float extinction)
    {
        EnsureShaderTestAvailable();
        const float farPlane=20000;
        int samples=CloudVolumeSettings.Default.ViewRaySampleMax;
        float budgetDistance=CloudVolumeSettings.Default.ViewBudgetDistance;
        var actual=Draw(extinction,width:200,secondColumn:true,sceneDistance:distance,liquidDistance:distance,
            viewSampleMax:samples,viewBudgetDistance:budgetDistance,farPlane:farPlane);
        // Recover the admitted span from independently authored camera bytes rather than
        // estimating distance from measured transmittance or borrowing shader ray helpers.
        float[] inverse=new float[16];Mat4f.Invert(inverse,Projection(farPlane));
        // Projection cancellation, lattice distances and every bucket product are
        // binary32 operations in the shader. Promoting the depth inversion to
        // binary64 changes the admitted span before the finite algorithm starts.
        float ndc=2*Depth(distance,farPlane)-1;
        float span=MathF.Abs((inverse[10]*ndc+inverse[14])/(inverse[11]*ndc+inverse[15]));
        span=MathF.Min(span,5050); // explicit inside origin, finite native exit along -Z
        float sigma=extinction/50,transmission=MathF.Exp(-sigma*span);
        int count=Math.Min(samples,Math.Max(CloudVolumeSettings.Default.ViewRaySampleMin,
            (int)Math.Floor(samples*Math.Min(1,span/budgetDistance))));
        float step=span/count,stepAlpha=1-MathF.Exp(-sigma*step);
        for(int bucket=0;bucket<3;bucket++)
        {
            float expected=1;
            for(int i=0;i<count;i++)
            {
                float t=i*step,adjusted=t>500?t-250:t*t*t*(1-t/1000)/250000;
                float bin=MathF.Log(1+adjusted/30),normalized=(bin-bucket)/.832f;
                float weight=bucket==2&&bin>2?1:MathF.Exp(-normalized*normalized);
                expected*=1-stepAlpha*weight;
            }
            double alpha=(1-transmission)*(1-expected);
            Assert.InRange(Math.Abs(actual.Buckets[bucket]-expected),0,2e-5);
            Assert.InRange(Math.Abs(actual.BucketAlpha[bucket]-alpha),0,2e-5);
            Assert.InRange(Math.Abs(actual.Source.X*actual.BucketAlpha[bucket]/10-alpha),0,2e-5);
        }
        Assert.InRange(Math.Abs(actual.Reveal-transmission),0,2e-5);
    }

    /// <summary>Native extinction and metre conversion weight the two occupied authored steps without changing the lattice.</summary>
    [Theory]
    [InlineData(.1f,1f)]
    [InlineData(2f,1f)]
    [InlineData(2f,10f)]
    [InlineData(4f,.01f)]
    public void ConstantColumnUsesAuthoredFiniteBudgetAndNativeUnits(float extinction,float units)
    {
        EnsureShaderTestAvailable();
        var result=Draw(extinction,units:units);
        // Four 25-unit intervals cover the finite 100-unit map; exactly two midpoints
        // lie in the occupied column. This is finite-lattice arithmetic, not exact
        // transport through an arbitrary discontinuous support field.
        double sigma=extinction/(50.0*units),stepTau=sigma*(25*units);
        Assert.InRange(Math.Abs(result.Reveal-Math.Exp(-2*stepTau)),0,.00002);
        Assert.InRange(result.Source.X,9.999f,10.001f);
        Assert.InRange(result.GlowAlpha,0,1);
        // Native k*(1-R) is emitted, not the ordinary surface depth-weighted output.
        float alpha=1-result.Reveal;
        Assert.InRange(Math.Abs(result.BucketAlpha.X-alpha*(1-result.Buckets.X)),0,.00001);
        // This authored complete span is 100 units, with two occupied 25-unit steps.
        // Evaluate the native products at the two accepted intervals' near depths.
        for(int bucket=0;bucket<3;bucket++)
        {
            double expected=1,stepAlpha=1-Math.Exp(-stepTau);
            foreach(double near in new double[]{0,25})
            {
                double adjusted=near*near*near*(1-near/1000)/250000;
                double bin=Math.Log(1+adjusted/30);
                double weight=bucket==2&&bin>2?1:Math.Exp(-Math.Pow((bin-bucket)/.832,2));
                expected*=1-stepAlpha*weight;
            }
            Assert.InRange(Math.Abs(result.Buckets[bucket]-expected),0,.00002);
        }
    }

    /// <summary>Empty, invalid and out-of-height columns contribute neither light nor coverage.</summary>
    [Theory]
    [InlineData(0f,0f)]
    [InlineData(-1f,0f)]
    [InlineData(float.NaN,0f)]
    [InlineData(float.PositiveInfinity,0f)]
    [InlineData(2f,100f)]
    public void InvalidOrOutsideDensityIsZero(float extinction,float altitude)
    {
        EnsureShaderTestAvailable(); var result=Draw(extinction,altitude:altitude);
        Assert.Equal(1,result.Reveal); Assert.Equal(Vector3.One,result.Buckets); Assert.Equal(Vector3.Zero,result.Source);
    }

    /// <summary>Invalid vertical envelopes and rays outside the finite grid never sample stretched border clouds.</summary>
    [Fact]
    public void InvalidBoundsAndUnavailableDomainAreRejected()
    {
        EnsureShaderTestAvailable();
        Assert.Equal(1,Draw(2,lower:float.NaN).Reveal);
        Assert.Equal(1,Draw(2,upper:float.PositiveInfinity).Reveal);
        Assert.Equal(1,Draw(2,lower:1,upper:-1).Reveal);
        Assert.Equal(1,Draw(2,offsetX:-51).Reveal);
        Assert.Equal(1,Draw(2,offsetX:51).Reveal);
        Assert.Equal(1,Draw(0,settings:CloudVolumeSettings.Default).Reveal);
    }

    /// <summary>Aligned occupied steps use their authored density; connected faces are not eroded.</summary>
    [Fact]
    public void SharedFacesAndAlignedOccupiedStepsUseNativeDensity()
    {
        EnsureShaderTestAvailable();
        var one=Draw(2);
        var two=Draw(1,secondColumn:true);
        // The first field accepts two 25-unit steps at extinction 2; the second
        // accepts four at extinction 1. Assert each finite sum independently.
        Assert.InRange(Math.Abs(one.Reveal-Math.Exp(-2*(2.0/50)*25)),0,.00002);
        Assert.InRange(Math.Abs(two.Reveal-Math.Exp(-4*(1.0/50)*25)),0,.00002);
        var settings=CloudVolumeSettings.Default with { DetailAmplitude=0 };
        var connected=Draw(1,secondColumn:true,settings:settings);
        Assert.InRange(Math.Abs(two.Reveal-connected.Reveal),0,.00002);
        var isolated=Draw(1,secondColumn:true,settings:settings,sharedNeighbor:false,offsetX:-.5f);
        Assert.InRange(Math.Abs(isolated.Reveal-Math.Exp(-.3125)),0,.00002);
        isolated=Draw(1,secondColumn:true,settings:settings,sharedNeighbor:false);
        Assert.Equal(1,isolated.Reveal); // the camera ray lies exactly on its exposed x face
    }

    /// <summary>The accepted 25-unit shadow interval feeds the declared HG and additional-octave arithmetic.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.5f)]
    public void SolarLunarAndOneOctaveUseTheSameDensity(float contribution)
    {
        EnsureShaderTestAvailable();
        var result=Draw(2,solar:30,lunar:3,settings:CloudVolumeSettings.Default with { Detail=false,OctaveContribution=contribution });
        double phase=.64/(4*Math.PI*Math.Pow(1.36,1.5));
        double expected=10+33*(phase*Math.Exp(-1)+contribution/(4*Math.PI)*Math.Exp(-.5));
        Assert.InRange(Math.Abs(result.Source.X-expected),0,.0002);
    }

    /// <summary>Anchored finite shadow intervals feed both sources and the octave endpoints without domain rescaling.</summary>
    [Theory]
    [InlineData(30f,0f,0f,0f,10,1500f)][InlineData(0f,30f,0f,1f,10,1500f)]
    [InlineData(30f,0f,0f,0f,64,3000f)][InlineData(30f,0f,0f,0f,64,10000f)]
    [InlineData(30f,3f,1f,0f,10,1500f)][InlineData(30f,3f,1f,1f,10,1500f)][InlineData(0f,0f,1f,0f,10,1500f)]
    [InlineData(30f,3f,1f,1f,64,3000f)][InlineData(30f,3f,1f,1f,64,10000f)]
    public void IsolatedShadowIlluminationUsesAnchoredFiniteBudget(float solar,float lunar,float contribution,float occlusion,int shadowCount,float shadowRange)
    {
        EnsureShaderTestAvailable();
        var result=Draw(2,solar:solar,lunar:lunar,settings:CloudVolumeSettings.Default with
            {Detail=false,OctaveContribution=contribution,OctaveOcclusion=occlusion},
            shadowSamples:shadowCount,shadowDistance:shadowRange,
            solarDirection:-Vector3.UnitZ,lunarDirection:-Vector3.UnitZ);
        // Two authored view steps at 12.5 and 37.5 units each represent 25 units.
        // Native support admits shadow midpoints only through the remaining 37.5
        // or 12.5 units. Every admitted midpoint retains its complete interval weight.
        double phase=.64/(4*Math.PI*Math.Pow(.16,1.5)),expected=0,total=1-Math.Exp(-2);
        for(int i=0;i<2;i++)
        {
            double tau=0,support=i==0?37.5:12.5;
            for(int sample=0;sample<shadowCount;sample++)
            {
                // Use binary32 interval coordinates so boundary ties are classified
                // in the same coordinate representation as the uploaded native map.
                float a=(float)sample/shadowCount,b=(float)(sample+1)/shadowCount;
                float near=shadowRange*a*a,far=shadowRange*b*b;
                if((near+far)*.5f<=support) tau+=(far-near)*2/50;
            }
            double source=10+(solar+lunar)*(phase*Math.Exp(-tau)+contribution/(4*Math.PI)*Math.Exp(-occlusion*tau));
            expected+=Math.Exp(-i)*(1-Math.Exp(-1))*source/total;
        }
        Assert.InRange(Math.Abs(result.Source.X-expected),0,.0002);
    }

    /// <summary>Exposed-face detail attenuates the view and both light rays through the identical density multiplier.</summary>
    [Fact]
    public void EdgeDetailIsSharedByViewAndLightTransport()
    {
        EnsureShaderTestAvailable();
        var result=Draw(1,secondColumn:true,sharedNeighbor:false,offsetX:-.5f,solar:30,lunar:3,
            settings:CloudVolumeSettings.Default with { DetailAmplitude=0 });
        double multiplier=.15625; // smoothstep(0,2,.5)
        double lightTau=.5*multiplier;
        double phase=.64/(4*Math.PI*Math.Pow(1.36,1.5));
        double expected=10+33*(phase*Math.Exp(-lightTau)+.5/(4*Math.PI)*Math.Exp(-.5*lightTau));
        Assert.InRange(Math.Abs(result.Reveal-Math.Exp(-2*multiplier)),0,.00002);
        Assert.InRange(Math.Abs(result.Source.X-expected),0,.0002);
    }

    /// <summary>Saturated extinction and the unoccluded octave endpoint cannot create zero-times-infinity output.</summary>
    [Fact]
    public void DenseColumnsRemainFiniteAtZeroOctaveOcclusion()
    {
        EnsureShaderTestAvailable();
        var result=Draw(float.MaxValue,solar:30,settings:CloudVolumeSettings.Default with { Detail=false,OctaveOcclusion=0 });
        Assert.Equal(0,result.Reveal);
        Assert.InRange(Math.Abs(result.Source.X-(10+30*.5/(4*Math.PI))),0,.0002);
    }

    /// <summary>Both opaque and normalized liquid depth clip the volume, including an inside-cloud start.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SceneAndLiquidDepthClipTransport(bool opaque)
    {
        EnsureShaderTestAvailable();
        var clipped=Draw(2,sceneDistance:opaque?25:1000,liquidDistance:opaque?1000:25);
        Assert.InRange(Math.Abs(clipped.Reveal-Math.Exp(-1)),0,.0002);
        var inside=Draw(2,offsetZ:-25);
        // The inside origin leaves a 75-unit primary span with three 25-unit
        // intervals; only its first midpoint remains inside the occupied column.
        Assert.InRange(Math.Abs(inside.Reveal-Math.Exp(-(2.0/50)*25)),0,.00002);
    }

    /// <summary>Corrected material depth cannot remove a surviving opaque particle's nearer visibility, and either depth can clip transport.</summary>
    [Theory]
    [InlineData(1000f,25f)][InlineData(25f,1000f)]
    public void SharedReceiverAndPrimaryVisibilityRemainDistinct(float receiverDistance,float visibilityDistance)
    {
        EnsureShaderTestAvailable();
        var clipped=Draw(2,sceneDistance:receiverDistance,visibilityDistance:visibilityDistance);
        Assert.InRange(Math.Abs(clipped.Reveal-Math.Exp(-1)),0,.0002);
    }

    /// <summary>Lookup decoding and affine aerial response enter once before final scene encoding.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(.125f)]
    public void AerialAndStorageAreAppliedOnce(float scale)
    {
        EnsureShaderTestAvailable(); var result=Draw(2,scale:scale,loss:.25f,scatter:2);
        Assert.InRange(Math.Abs(result.Source.X-9.5f*scale),0,.0002);
        Assert.InRange(Math.Abs(result.Reveal-Math.Exp(-2)),0,.00002);
    }

    /// <summary>Underwater medium transport replaces aerial transport rather than applying both attenuation paths.</summary>
    [Fact]
    public void UnderwaterUsesLocalFogInsteadOfAerial()
    {
        EnsureShaderTestAvailable();
        var result=Draw(2,underwater:true,fogDensity:.02f,loss:.99f,scatter:100);
        // The two contributing midpoints are 12.5 and 37.5; their opacity weights differ by exp(-1).
        // Apply local fog once at that opacity centroid, independently of source brightness.
        double mean=(12.5+Math.Exp(-1)*37.5)/(1+Math.Exp(-1));
        double expected=10*Math.Exp(-.02*mean);
        Assert.InRange(Math.Abs(result.Source.X-expected),0,.0002);
        Assert.InRange(Math.Abs(result.Reveal-Math.Exp(-2)),0,.00002);
    }

    /// <summary>Rendering uses the same physical inputs when the surrounding receiver system enables or disables LumOn.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothPbrModesUseIndependentCloudTransport(bool lumOn)
    {
        EnsureShaderTestAvailable();
        var previous=PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled=lumOn;
            var result=Draw(2);
            Assert.InRange(Math.Abs(result.Reveal-Math.Exp(-2)),0,.00002);
            Assert.InRange(result.Source.X,9.999f,10.001f);
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled=previous; }
    }

    /// <summary>Density obeys its declared multiplier bounds and material anchoring cancels recentering and committed wind.</summary>
    [Fact]
    public void NoiseIsBoundedAndStableAcrossRecentering()
    {
        EnsureShaderTestAvailable();
        var a=Draw(.6f,width:4,secondColumn:true,settings:CloudVolumeSettings.Default);
        var recentered=Draw(.6f,width:4,secondColumn:true,settings:CloudVolumeSettings.Default,centerX:1,offsetX:50);
        Assert.InRange(Math.Abs(a.Reveal-recentered.Reveal),0,.00002);
        double tau=-Math.Log(a.Reveal);
        // All seven scheduled points are inside support and farther than the
        // two-unit exposed-edge softness. Every density multiplier therefore
        // lies in the declared [1-amplitude,1+amplitude] interval.
        Assert.InRange(tau,.6*3*.85,.6*3*1.15);
        var wind=Draw(.6f,width:4,secondColumn:true,settings:CloudVolumeSettings.Default,offsetX:3);
        var recenteredWind=Draw(.6f,width:4,secondColumn:true,settings:CloudVolumeSettings.Default,centerX:1,offsetX:53);
        Assert.InRange(Math.Abs(wind.Reveal-recenteredWind.Reveal),0,.00002);
        var committedWind=Draw(.6f,width:4,secondColumn:true,settings:CloudVolumeSettings.Default,offsetX:-50,windTileX:1);
        Assert.InRange(Math.Abs(a.Reveal-committedWind.Reveal),0,.00002);
    }

    /// <summary>Vertical rays reject out-of-height samples and retain their scheduled interval weights without horizontal division.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VerticalRaysRejectOutsideHeightAndUseFiniteWeights(bool above)
    {
        EnsureShaderTestAvailable();
        float[] view=above?[1,0,0,0,0,0,1,0,0,-1,0,0,0,0,0,1]:[1,0,0,0,0,0,-1,0,0,1,0,0,0,0,0,1];
        var result=Draw(2,altitude:above?-100:100,offsetZ:0,view:view,secondColumn:true,sceneDistance:200,
            settings:CloudVolumeSettings.Default with {Detail=false},viewSampleMax:8,viewBudgetDistance:50);
        // Eight 25-unit intervals cover the authored 200-unit ray; only the
        // midpoints at 87.5 and 112.5 fall inside the fifty-unit vertical bounds.
        Assert.InRange(Math.Abs(result.Reveal-Math.Exp(-2*(2.0/50)*25)),0,.00003);
    }
    #endregion

    #region Private
    /// <summary>Separates straight encoded radiance from native premultiplied bin weights and dimensionless coverage.</summary>
    private sealed record Result(Vector3 Source,float Reveal,Vector3 Buckets,Vector3 BucketAlpha,float GlowAlpha);

    /// <summary>Executes authored scalar presets: 96/2000 primary and two quadratic shadow intervals over 100 units.</summary>
    private Result Draw(float extinction,float units=1,int width=2,bool secondColumn=false,bool sharedNeighbor=true,
        float altitude=0,float offsetZ=-50,float offsetX=0,int centerX=0,CloudVolumeSettings? settings=null,
        float solar=0,float lunar=0,float scale=1,float loss=0,float scatter=0,float sceneDistance=1000,float liquidDistance=1000,
        float[]? view=null,bool underwater=false,float fogDensity=0,float lower=-.5f,float upper=.5f,int windTileX=0,float? visibilityDistance=null,
        int viewSampleMax=96,float viewBudgetDistance=2000,float farPlane=1000,
        int shadowSamples=2,float shadowDistance=100,Vector3? solarDirection=null,Vector3? lunarDirection=null)
    {
        float[] projection=Projection(farPlane);
        float[] inverse=new float[16]; Mat4f.Invert(inverse,projection);
        using var camera=TestFrameCamera.Create(inverse,view??Mat4f.Create(),1,1,.1f,farPlane);
        using var storage=new TestFrameLightingPublication(); storage.Capture(scale);
        var program=Programs.Create<CloudVolumeProgram>(); program.Frame=camera; program.Lighting=storage.Buffer;
        var placement=new CloudPlacement(centerX,0,0,0,windTileX,0,0,0,new(offsetX,altitude,offsetZ),0);
        var quality=(settings??(CloudVolumeSettings.Default with {Detail=false})) with
        {ViewRaySampleMax=viewSampleMax,ViewRaySampleMin=2,ViewBudgetDistance=viewBudgetDistance,LightRaySampleMax=shadowSamples,ShadowDistance=shadowDistance};
        program.Parameters.CaptureMap(width,placement,units,quality);
        Assert.Equal((settings?.Detail??false)?1:0,MemoryMarshal.Cast<byte,float>(program.Parameters.Bytes)[11]);
        var lighting=new AtmosphereLighting(solarDirection??Vector3.UnitY,new(solar),new(10),Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
        { Lunar=new(lunar),Moon=lunarDirection??-Vector3.UnitY,CalibrationGeneration=1,LookupStorageScale=LightingStorage.PersistentScale,ReconstructionMaximumNits=scatter };
        program.Parameters.CaptureLighting(lighting,underwater?1:0,new(fogDensity,0,0,0),Vector3.Zero);
        using var map=DynamicTexture2D.Create(width,width,PixelInternalFormat.Rgba32f);
        float[] columns=new float[width*width*4];
        for(int z=0;z<width;z++) for(int x=0;x<width;x++)
        {
            int index=(z*width+x)*4;
            columns[index]=(secondColumn||z==width-1)&&(sharedNeighbor||x==width/2)?extinction:0;
            columns[index+2]=lower; columns[index+3]=upper;
        }
        map.UploadDataImmediate(columns);

        using var opaque=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        using var visibility=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        using var liquid=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        opaque.UploadDataImmediate(new[]{Depth(sceneDistance,farPlane)}); liquid.UploadDataImmediate(new[]{Depth(liquidDistance,farPlane)});
        visibility.UploadDataImmediate([Depth(visibilityDistance??sceneDistance,farPlane)]);
        using var aerial=DynamicTexture3D.Create(1,2,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D);
        using var attenuation=DynamicTexture3D.Create(1,1,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D);
        using var shafts=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        aerial.UploadDataImmediate([scatter/16,scatter/16,scatter/16,0,0,0,0,0],0,0,0,1,2,1,0);
        attenuation.UploadDataImmediate([loss,loss,loss,0],0,0,0,1,1,1,0); shafts.UploadDataImmediate(new float[]{0});
        program.Map=map.TextureId; program.SceneDepth=opaque.TextureId; program.VisibilityDepth=visibility.TextureId; program.LiquidDepth=liquid.TextureId;
        program.AerialRadiance=aerial; program.AerialAttenuation=attenuation; program.LightShafts=shafts;
        using var reveal=DynamicTexture2D.Create(1,1,PixelInternalFormat.Rgb32f);
        using var overall=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        using var glow=DynamicTexture2D.Create(1,1,PixelInternalFormat.Rgba32f);
        using var a0=DynamicTexture2D.Create(1,1,PixelInternalFormat.Rgba32f);
        using var a1=DynamicTexture2D.Create(1,1,PixelInternalFormat.Rgba32f);
        using var a2=DynamicTexture2D.Create(1,1,PixelInternalFormat.Rgba32f);
        using var target=DynamicGpuFramebuffer.CreateMRT([reveal,overall,glow,a0,a1,a2])!;
        var pass=new RenderPassDesc(target,[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),
            new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),new(2,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(3,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(4,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(5,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
        using var metadata=new RenderPassTargets(pass); using var lifetime=new GraphicsPipelineLifetime();
        using var pipeline=new GraphicsPipeline(lifetime,new(program.GraphicsIdentity!,CloudVolumeRenderer.Layout,metadata.Signature,
            DynamicPipelineState.Viewport,blending:LiquidPipelineStates.SurfaceBlending),program);
        using var geometry=new ArrayGraphicsGeometry(CloudVolumeRenderer.Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
        int priorVao=StateCache.Current.GetCurrentVao(),priorFbo=StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer);
        Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.Volume",[pipeline],true,commands=>
        {
            commands.BeginPass(pass); commands.SetPipeline(pipeline); commands.SetDynamicState(new(){Viewport=commands.PassViewport});
            commands.Draw(geometry,new(0,3)); commands.EndPass();
        }));
        Assert.Equal(priorVao,StateCache.Current.GetCurrentVao()); Assert.Equal(priorFbo,StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
        float[] c=a0.ReadPixels(),b=reveal.ReadPixels(),r=overall.ReadPixels(),g=glow.ReadPixels(),
            second=a1.ReadPixels(),third=a2.ReadPixels();
        foreach(var image in new[]{c,b,r,g,second,third}) Assert.All(image,value=>Assert.True(float.IsFinite(value)&&value>=0));
        Assert.All(b,value=>Assert.InRange(value,0,1)); Assert.InRange(r[0],0,1);
        Assert.True(map.IsValid); GlDebug.ThrowIfErrors("Cloud volume transport");
        return new(c[3]>0?new Vector3(c[0],c[1],c[2])/c[3]:Vector3.Zero,r[0],new(b[0],b[1],b[2]),new(c[3],second[3],third[3]),g[3]);
    }

    /// <summary>Encodes an independently authored perspective depth for a surface at the requested camera distance.</summary>
    private static float Depth(float distance,float farPlane=1000)
    {
        float a=farPlane==1000?1.00020002f:(float)((farPlane+.1)/(farPlane-.1));
        float b=farPlane==1000?.200020002f:(float)(.2*farPlane/(farPlane-.1));
        return (a-b/distance)*.5f+.5f;
    }
    /// <summary>Authors perspective constants independently, retaining the established scalar-fixture projection exactly.</summary>
    private static float[] Projection(float farPlane)
    {
        float a=farPlane==1000?1.00020002f:(float)((farPlane+.1)/(farPlane-.1));
        float b=farPlane==1000?.200020002f:(float)(.2*farPlane/(farPlane-.1));
        return [1,0,0,0,0,1,0,0,0,0,-a,-1,0,0,-b,0];
    }
    #endregion
}


