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

/// <summary>Checks actual reference shader transport against independently computed optical and OIT results.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CloudVolumeDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Native r remains extinction rather than alpha for all positive values and metre conversions.</summary>
    [Theory]
    [InlineData(.1f,1f)]
    [InlineData(2f,1f)]
    [InlineData(2f,10f)]
    [InlineData(4f,.01f)]
    public void ConstantColumnMatchesNativeBeerLambert(float extinction,float units)
    {
        EnsureShaderTestAvailable();
        var result=Draw(extinction,units:units);
        Assert.InRange(Math.Abs(result.Reveal-Math.Exp(-extinction)),0,.00002);
        Assert.InRange(result.Source.X,9.999f,10.001f);
        Assert.InRange(result.GlowAlpha,0,1);
        // Native k*(1-R) is emitted, not the ordinary surface depth-weighted output.
        float alpha=1-result.Reveal;
        Assert.InRange(Math.Abs(result.BucketAlpha.X-alpha*(1-result.Buckets.X)),0,.00001);
        Assert.InRange(Math.Abs(result.Buckets.X-(1-alpha)),0,.00002); // entry distance is zero
        Assert.InRange(Math.Abs(result.Buckets.Y-(1-alpha*Math.Exp(-Math.Pow(1/.832,2)))),0,.00002);
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

    /// <summary>Subdivision and a different column arrangement retain optical thickness; connected faces are not eroded.</summary>
    [Fact]
    public void SharedFacesAndPathSubdivisionPreserveConstantDensity()
    {
        EnsureShaderTestAvailable();
        var one=Draw(2);
        var two=Draw(1,secondColumn:true);
        Assert.InRange(Math.Abs(one.Reveal-two.Reveal),0,.00002);
        var settings=CloudVolumeSettings.Default with { DetailAmplitude=0 };
        var connected=Draw(1,secondColumn:true,settings:settings);
        Assert.InRange(Math.Abs(two.Reveal-connected.Reveal),0,.00002);
        var isolated=Draw(1,secondColumn:true,settings:settings,sharedNeighbor:false,offsetX:-.5f);
        Assert.InRange(Math.Abs(isolated.Reveal-Math.Exp(-.3125)),0,.00002);
        isolated=Draw(1,secondColumn:true,settings:settings,sharedNeighbor:false);
        Assert.Equal(1,isolated.Reveal); // the camera ray lies exactly on its exposed x face
    }

    /// <summary>One approximate octave adds its declared response beyond shadowed HG single scattering.</summary>
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
        Assert.InRange(Math.Abs(inside.Reveal-Math.Exp(-1)),0,.00002);
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

    /// <summary>Lookup decoding, affine aerial transport and frozen storage enter once for each covered interval.</summary>
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
        Assert.InRange(Math.Abs(result.Source.X-10*Math.Exp(-.5)),0,.0002);
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

    /// <summary>Material anchoring cancels recentering, while fractional native wind moves detail once.</summary>
    [Fact]
    public void NoiseIsBoundedAndStableAcrossRecentering()
    {
        EnsureShaderTestAvailable();
        var a=Draw(.6f,width:4,secondColumn:true,settings:CloudVolumeSettings.Default);
        var recentered=Draw(.6f,width:4,secondColumn:true,settings:CloudVolumeSettings.Default,centerX:1,offsetX:50);
        Assert.InRange(Math.Abs(a.Reveal-recentered.Reveal),0,.00002);
        double tau=-Math.Log(a.Reveal);
        Assert.InRange(tau,.6*3*.85,.6*3*1.15);
        var wind=Draw(.6f,width:4,secondColumn:true,settings:CloudVolumeSettings.Default,offsetX:3);
        Assert.True(Math.Abs(a.Reveal-wind.Reveal)>1e-6);
        var committedWind=Draw(.6f,width:4,secondColumn:true,settings:CloudVolumeSettings.Default,offsetX:-50,windTileX:1);
        Assert.InRange(Math.Abs(a.Reveal-committedWind.Reveal),0,.00002);
    }

    /// <summary>Purely vertical rays enter from above/below and never depend on a horizontal division by zero.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VerticalRaysUseActualColumnBounds(bool above)
    {
        EnsureShaderTestAvailable();
        float[] view=above?[1,0,0,0,0,0,1,0,0,-1,0,0,0,0,0,1]:[1,0,0,0,0,0,-1,0,0,1,0,0,0,0,0,1];
        var result=Draw(2,altitude:above?-100:100,offsetZ:0,view:view,secondColumn:true);
        Assert.InRange(Math.Abs(result.Reveal-Math.Exp(-2)),0,.00003);
    }
    #endregion

    #region Private
    /// <summary>Separates straight encoded radiance from native premultiplied bin weights and dimensionless coverage.</summary>
    private sealed record Result(Vector3 Source,float Reveal,Vector3 Buckets,Vector3 BucketAlpha,float GlowAlpha);

    /// <summary>Executes a one-pixel full-resolution reference with independent native column fixtures and owned GPU state.</summary>
    private Result Draw(float extinction,float units=1,int width=2,bool secondColumn=false,bool sharedNeighbor=true,
        float altitude=0,float offsetZ=-50,float offsetX=0,int centerX=0,CloudVolumeSettings? settings=null,
        float solar=0,float lunar=0,float scale=1,float loss=0,float scatter=0,float sceneDistance=1000,float liquidDistance=1000,
        float[]? view=null,bool underwater=false,float fogDensity=0,float lower=-.5f,float upper=.5f,int windTileX=0,float? visibilityDistance=null)
    {
        float[] projection=[1,0,0,0,0,1,0,0,0,0,-1.00020002f,-1,0,0,-.200020002f,0];
        float[] inverse=new float[16]; Mat4f.Invert(inverse,projection);
        using var camera=TestFrameCamera.Create(inverse,view??Mat4f.Create(),1,1,.1f,1000);
        using var storage=new TestFrameLightingPublication(); storage.Capture(scale);
        var program=Programs.Create<CloudVolumeProgram>(); program.Frame=camera; program.Lighting=storage.Buffer;
        var placement=new CloudPlacement(centerX,0,0,0,windTileX,0,0,0,new(offsetX,altitude,offsetZ),0);
        program.Parameters.CaptureMap(width,placement,units,settings??(CloudVolumeSettings.Default with { Detail=false }));
        Assert.Equal((settings?.Detail??false)?1:0,MemoryMarshal.Cast<byte,float>(program.Parameters.Bytes)[11]);
        var lighting=new AtmosphereLighting(Vector3.UnitY,new(solar),new(10),Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
        { Lunar=new(lunar),Moon=-Vector3.UnitY,CalibrationGeneration=1,LookupStorageScale=LightingStorage.PersistentScale,ReconstructionMaximumNits=scatter };
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
        Assert.Equal(columns,map.ReadPixels());
        using var opaque=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        using var visibility=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        using var liquid=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        opaque.UploadDataImmediate(new[]{Depth(sceneDistance)}); liquid.UploadDataImmediate(new[]{Depth(liquidDistance)});
        visibility.UploadDataImmediate([Depth(visibilityDistance??sceneDistance)]);
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
        using var target=GpuFramebuffer.CreateMRT([reveal,overall,glow,a0,a1,a2])!;
        var pass=new RenderPassDesc(target,[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),
            new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),new(2,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(3,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(4,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(5,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
        using var metadata=new RenderPassTargets(pass); using var lifetime=new GraphicsPipelineLifetime();
        using var pipeline=new GraphicsPipeline(lifetime,new(program.GraphicsIdentity!,CloudVolumeRenderer.Layout,metadata.Signature,
            DynamicPipelineState.Viewport,blending:LiquidPipelineStates.SurfaceBlending),program);
        using var geometry=new ArrayGraphicsGeometry(CloudVolumeRenderer.Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
        int priorVao=GL.GetInteger(GetPName.VertexArrayBinding),priorFbo=GL.GetInteger(GetPName.DrawFramebufferBinding);
        Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.Volume",[pipeline],true,commands=>
        {
            commands.BeginPass(pass); commands.SetPipeline(pipeline); commands.SetDynamicState(new(){Viewport=commands.PassViewport});
            commands.Draw(geometry,new(0,3)); commands.EndPass();
        }));
        Assert.Equal(priorVao,GL.GetInteger(GetPName.VertexArrayBinding)); Assert.Equal(priorFbo,GL.GetInteger(GetPName.DrawFramebufferBinding));
        float[] c=a0.ReadPixels(),b=reveal.ReadPixels(),r=overall.ReadPixels(),g=glow.ReadPixels();
        foreach(var image in new[]{c,b,r,g,a1.ReadPixels(),a2.ReadPixels()}) Assert.All(image,value=>Assert.True(float.IsFinite(value)&&value>=0));
        Assert.All(b,value=>Assert.InRange(value,0,1)); Assert.InRange(r[0],0,1);
        Assert.True(GL.IsTexture(map.TextureId)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        return new(c[3]>0?new Vector3(c[0],c[1],c[2])/c[3]:Vector3.Zero,r[0],new(b[0],b[1],b[2]),new(c[3],a1.ReadPixels()[3],a2.ReadPixels()[3]),g[3]);
    }

    /// <summary>Encodes an independently authored perspective depth for a surface at the requested camera distance.</summary>
    private static float Depth(float distance) => (1.00020002f-.200020002f/distance)*.5f+.5f;
    #endregion
}


