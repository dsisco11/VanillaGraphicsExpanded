using System.Numerics;
using System.Collections.Immutable;
using System.Reflection;
using Moq;
using HarmonyLib;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes production celestial programs with independent footprint and angular-energy references.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CelestialDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Persistent instance geometry exposes exactly the selected prefix and retires with the completed source owner.</summary>
    [Fact]
    public void InstanceGeometryRetainsSelectedPrefixUntilDisposal()
    {
        EnsureContextValid();
        var source=CelestialSource.Create(new(0,32,1024,4));
        using var stars=new CelestialStarGeometry(source);
        Assert.Equal(1024,stars.Count);
        var description=new GraphicsPipelineDesc(Programs.Create<CelestialStarProgram>().GraphicsIdentity!,CelestialStarGeometry.Layout,
            new([new(PixelInternalFormat.Rgba32f),new(PixelInternalFormat.Rgba32f)]),DynamicPipelineState.Viewport);
        stars.Geometry.Validate(description,new(0,6,InstanceCount:stars.Count));
        Assert.Throws<ArgumentOutOfRangeException>(()=>stars.Geometry.Validate(description,new(0,6,InstanceCount:stars.Count+1)));
        stars.Dispose();Assert.Throws<ObjectDisposedException>(()=>stars.Geometry.Validate(description,new(0,6,InstanceCount:stars.Count)));
    }
    /// <summary>Actual star sampling agrees with admitted atmospheric transport at off-row elevations in clear and cloudy air.</summary>
    [Theory]
    [InlineData(.001f,0f)]
    [InlineData(.001f,.5f)]
    [InlineData(1f,0f)]
    [InlineData(1f,.75f)]
    public void InterpolatedTransmissionMatchesExistingTransport(float altitude,float clouds)
    {
        EnsureShaderTestAvailable();
        float horizon=AtmosphereSkyMapping.Horizon(altitude);
        var lighting=new AtmosphereLighting(Vector3.UnitY,Vector3.Zero,Vector3.Zero,Vector3.Zero,Vector3.One,ImmutableArray<float>.Empty)
            {Altitude=altitude,HorizonElevation=horizon,CloudCoverage=clouds,Width=1,Height=1,
                Sky=ImmutableArray.Create(0f,0f,0f,1f),ExternalTransmission=AtmosphereExternalTransmission.Build(altitude,1+7*clouds)};
        using var textures=new AtmosphereTextureSet(lighting);textures.Upload(lighting);
        foreach(float elevation in new[]{.015f,.123f,.51f})
        {
            Vector2 centre=new(32,32+32/MathF.Tan(1)*MathF.Tan(elevation));
            Vector3 expected=AtmosphereModel.ExternalTransmittance(new(0,MathF.Sin(elevation),-MathF.Cos(elevation)),altitude,1+7*clouds)
                * new Vector3(.001f,.0005f,.00025f);
            Vector3 actual=DrawStar(64,2,centre,1,Vector3.One,false,textures.ExternalTransmission,horizon);
            for(int c=0;c<3;c++)Assert.InRange(Math.Abs(actual[c]-expected[c]),0,Math.Max(expected[c]*.01f,1e-9f));
        }
        Assert.Equal(Vector3.Zero,DrawStar(64,2,new(32,16),1,Vector3.One,false,textures.ExternalTransmission,horizon));
    }
    /// <summary>Storage admission includes every instance and rejects unsupported tiny or orthographic views.</summary>
    [Theory]
    [InlineData(0,1024,4)]
    [InlineData(1,2048,5)]
    [InlineData(2,3072,6)]
    [InlineData(3,4096,7)]
    public void AdmissionBoundsAllOverlapAndRejectsUnsupportedProjection(int quality,int count,int octaves)
    {
        EnsureContextValid();
        var source=CelestialSource.Create(new(quality,32,count,octaves));
        using var frame=TestFrameCamera.CreateFromProjection(Projection(64,1),64,64);
        float bound=CelestialDrawAdmission.MaximumNits(source,frame);
        // Accumulate the displayed prefix independently, preserving each FP32
        // peak before widening the sum; omitted stars belong to the galaxy map.
        double flux=0;
        foreach(var star in source.Stars.Take(count))
            flux+=star.FluxLux*Math.Max(star.Color.X,Math.Max(star.Color.Y,star.Color.Z));
        Assert.Equal(flux,source.ResolvedStarMaximumFluxLux);
        float p=Projection(64,1).M11;
        double corner=1.0625/p,area=4/(64*64*p*p*Math.Pow(1+2*corner*corner,1.5));
        Assert.True(bound>=source.MaximumNits+flux/area);
        using var orthographic=TestFrameCamera.CreateIdentity(64,64);
        Assert.Throws<ArgumentException>(()=>CelestialDrawAdmission.MaximumNits(source,orthographic));
        using var tiny=TestFrameCamera.CreateFromProjection(Projection(16,1),16,16);
        Assert.Throws<ArgumentException>(()=>CelestialDrawAdmission.MaximumNits(source,tiny));
    }
    /// <summary>Owned rendering follows its independent lifetime through startup, preparation failure, reset and teardown.</summary>
    [Fact]
    public void OwnedRendererRetainsLifetimeThroughFailureAndReset()
    {
        EnsureContextValid();
        Assert.False(CelestialRenderer.IsEnabled);
        var api=new Mock<ICoreClientAPI>{DefaultValue=DefaultValue.Mock};
        using(var renderer=new CelestialRenderer(api.Object,null!))
        {
            Assert.True(CelestialRenderer.IsEnabled);
            renderer.OnRenderFrame(0,EnumRenderStage.Before);
            Assert.True(CelestialRenderer.IsEnabled);
            ScreenResourceManager.HandleScreenResize();
            Assert.True(CelestialRenderer.IsEnabled);
            typeof(CelestialRenderer).GetMethod("Reload",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(renderer,null);
            Assert.True(CelestialRenderer.IsEnabled);
            Mock.Get(api.Object.Event).Raise(value=>value.LeaveWorld+=null);
            Assert.True(CelestialRenderer.IsEnabled);
            Assert.Equal(0,CelestialRenderer.MaximumNits);
        }
        Assert.False(CelestialRenderer.IsEnabled);
    }
    /// <summary>Atmosphere owns transmission storage and uploads dimensionless values without its radiance scale.</summary>
    [Fact]
    public void TransmissionTracksCompletedMediumWithoutPerFrameAllocation()
    {
        EnsureContextValid();
        var lighting=new AtmosphereLighting(Vector3.UnitY,Vector3.Zero,Vector3.Zero,Vector3.Zero,Vector3.One,ImmutableArray<float>.Empty)
            {Altitude=.001f,HorizonElevation=AtmosphereSkyMapping.Horizon(.001f),CloudCoverage=.25f,Width=1,Height=1,
                Sky=ImmutableArray.Create(0f,0f,0f,1f),ExternalTransmission=AtmosphereExternalTransmission.Build(.001f,2.75f)};
        using var textures=new AtmosphereTextureSet(lighting);
        textures.Upload(lighting);var first=textures.ExternalTransmission;textures.Upload(lighting);
        Assert.Same(first,textures.ExternalTransmission);
        float[] data=first.ReadPixels();
        foreach(int i in new[]{0,64,255,511})
        {
            float e=AtmosphereSkyMapping.Elevation(i/511f,lighting.HorizonElevation);
            Vector3 expected=AtmosphereModel.ExternalTransmittance(new(MathF.Cos(e),MathF.Sin(e),0),lighting.Altitude,2.75f);
            for(int c=0;c<3;c++)Assert.InRange(Math.Abs(data[4*i+c]-expected[c]),0,.000001f);
        }
        textures.Upload(lighting with{CloudCoverage=.5f,ExternalTransmission=AtmosphereExternalTransmission.Build(.001f,4.5f)});
        Assert.Same(first,textures.ExternalTransmission);
        Assert.False(data.SequenceEqual(first.ReadPixels()));
        textures.Dispose();Assert.False(first.IsValid);
    }
    /// <summary>Resolution, field of view, off-axis position and subpixel phase preserve calibrated RGB flux.</summary>
    [Theory]
    [InlineData(32, .7f, .5f, .5f)]
    [InlineData(64, 1.4f, .431f, .697f)]
    [InlineData(96, 2f, .9f, .85f)]
    public void IntegratedStarFluxIsCameraIndependent(int size, float fov, float x, float y)
    {
        EnsureShaderTestAvailable();
        Vector3 expected = new(.001f,.0005f,.00025f);
        Vector3 actual = DrawStar(size,fov,new(size*x,size*y),1,Vector3.One,false);
        AssertEnergy(expected,actual);
    }

    /// <summary>Overlapping instances add linearly, and external extinction attenuates each RGB channel exactly once.</summary>
    [Fact]
    public void OverlapAndTransmissionPreserveLinearEnergy()
    {
        EnsureShaderTestAvailable();
        Vector3 actual = DrawStar(64,1,new(31.23f,33.81f),3,new(.5f,.25f,.75f),false);
        AssertEnergy(new(.0015f,.000375f,.0005625f),actual);
    }

    /// <summary>A partially visible tent loses its clipped power without renormalization, and rear-facing stars draw nothing.</summary>
    [Fact]
    public void ClippedAndBehindCameraFootprintsAreRejectedConservatively()
    {
        EnsureShaderTestAvailable();
        Vector3 clipped = DrawStar(32,1,new(-.25f,16.3f),1,Vector3.One,false);
        AssertEnergy(new Vector3(.001f,.0005f,.00025f)*.28125f,clipped);
        Assert.Equal(Vector3.Zero,DrawStar(32,1,new(16,16),1,Vector3.One,true));
    }

    /// <summary>The fullscreen program decodes its generated cube once and uses the inverse celestial orientation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GalaxyRotationAndEncodingSelectTheCorrectFace(bool rotated)
    {
        EnsureShaderTestAvailable();
        var shader = Programs.Create<CelestialGalaxyProgram>();
        using var camera = TestFrameCamera.CreateFromProjection(Projection(32,.6f),32,32);
        shader.Frame = camera;
        shader.DrawInputs.Capture(rotated?Matrix4x4.CreateRotationY(MathF.PI/2):Matrix4x4.Identity,.125f,-MathF.PI/2);
        using var cube = DynamicTextureCube.Create(2,PixelInternalFormat.Rgba32f);
        for(int face=0;face<6;face++)
        {
            float[] values = Enumerable.Range(0,4).SelectMany(_=>new[]{(float)(face+1),2f,4f,1f}).ToArray();
            using var binding=StateCache.Current.BindTextureScope(TextureTarget.TextureCubeMap,0,cube.TextureId);
            GL.TexSubImage2D(TextureTarget.TextureCubeMapPositiveX+face,0,0,0,2,2,PixelFormat.Rgba,PixelType.Float,values);
        }
        using var transmission=DynamicTexture2D.CreateWithData(2,1,PixelInternalFormat.Rgba32f,[.5f,.25f,.75f,1,.5f,.25f,.75f,1]);
        shader.GalaxyMap=cube;shader.ExternalTransmission=transmission;
        using var geometry=new ArrayGraphicsGeometry(CelestialRenderer.FullscreenLayout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),3);
        float[] actual=Draw(shader,geometry,CelestialRenderer.FullscreenLayout,32,1);
        float red=(rotated?1f:6f)*.125f*.5f;
        for(int i=0;i<actual.Length;i+=4)
        {
            Assert.InRange(actual[i],red-.0001f,red+.0001f);
            Assert.InRange(actual[i+1],.06249f,.06251f);
            Assert.InRange(actual[i+2],.37499f,.37501f);
        }
    }
    #endregion

    #region Private
    /// <summary>Authors a conventional perspective camera independently of the production admission calculation.</summary>
    private static Matrix4x4 Projection(int size,float fov)
    {
        float p=1/MathF.Tan(fov*.5f);
        return new(p,0,0,0,0,p,0,0,0,0,-1,-1,0,0,-.2f,0);
    }
    /// <summary>Uploads controlled instances and integrates readback using independently reconstructed perspective solid angles.</summary>
    private Vector3 DrawStar(int size,float fov,Vector2 centre,int count,Vector3 transmission,bool behind,DynamicTexture2D? table=null,float horizon=-MathF.PI/2)
    {
        var shader=Programs.Create<CelestialStarProgram>();
        Matrix4x4 projection=Projection(size,fov);
        using var camera=TestFrameCamera.CreateFromProjection(projection,size,size);
        shader.Frame=camera;shader.DrawInputs.Capture(Matrix4x4.Identity,1,horizon);
        using var extinction=DynamicTexture2D.CreateWithData(2,1,PixelInternalFormat.Rgba32f,
            [transmission.X,transmission.Y,transmission.Z,1,transmission.X,transmission.Y,transmission.Z,1]);
        shader.ExternalTransmission=table??extinction;
        Vector3 direction=Vector3.Normalize(new((centre.X/size*2-1)/projection.M11,(centre.Y/size*2-1)/projection.M22,behind?1:-1));
        using var buffer=GpuVbo.Create();
        buffer.UploadData(Enumerable.Range(0,count).SelectMany(_=>new[]{new Vector4(direction,.001f),new Vector4(1,.5f,.25f,0)}).ToArray());
        using var geometry=new ArrayGraphicsGeometry(CelestialStarGeometry.Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>{[0]=buffer});
        float[] data=Draw(shader,geometry,CelestialStarGeometry.Layout,size,count);
        double[] integrated=new double[3];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            double lowX=(2.0*x/size-1)/projection.M11,highX=(2.0*(x+1)/size-1)/projection.M11;
            double lowY=(2.0*y/size-1)/projection.M22,highY=(2.0*(y+1)/size-1)/projection.M22;
            double area=CornerArea(highX,highY)-CornerArea(lowX,highY)-CornerArea(highX,lowY)+CornerArea(lowX,lowY);
            for(int c=0;c<3;c++)integrated[c]+=data[(y*size+x)*4+c]*area;
        }
        return new((float)integrated[0],(float)integrated[1],(float)integrated[2]);
    }
    /// <summary>Integrates a rectangular perspective pixel analytically in double precision.</summary>
    private static double CornerArea(double x,double y)=>Math.Atan2(x*y,Math.Sqrt(1+x*x+y*y));
    /// <summary>Submits actual prepared graphics pipelines and verifies glow output remains empty.</summary>
    private float[] Draw(GpuProgram shader,ArrayGraphicsGeometry geometry,VertexLayoutDesc layout,int size,int count)
    {
        using var color=DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var glow=DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var target=DynamicGpuFramebuffer.CreateMRT([color,glow])!;
        using var lifetime=new GraphicsPipelineLifetime();
        var pass=new RenderPassDesc(target,[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
        using var metadata=new RenderPassTargets(pass);
        var blend=new ColorBlendDesc{Enabled=true,SourceRgb=BlendingFactorSrc.One,DestinationRgb=BlendingFactorDest.One};
        using var pipeline=new GraphicsPipeline(lifetime,new(shader.GraphicsIdentity!,layout,metadata.Signature,DynamicPipelineState.Viewport,blending:[blend,blend]),shader);
        int priorFramebuffer=GL.GetInteger(GetPName.DrawFramebufferBinding);
        bool priorDepth=GL.IsEnabled(EnableCap.DepthTest),priorBlend=GL.IsEnabled(EnableCap.Blend);
        GL.GetBoolean(GetPName.DepthWritemask,out bool priorWrites);
        Assert.True(GraphicsCommandContext.TryRun("Tests.CelestialDraw",[pipeline],true,commands=>
        {
            commands.BeginPass(pass);commands.SetPipeline(pipeline);commands.SetDynamicState(new(){Viewport=commands.PassViewport});
            commands.Draw(geometry,new(0,layout==CelestialRenderer.FullscreenLayout?3:6,InstanceCount:count));commands.EndPass();
        }));
        Assert.Equal(priorFramebuffer,GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(priorDepth,GL.IsEnabled(EnableCap.DepthTest));Assert.Equal(priorBlend,GL.IsEnabled(EnableCap.Blend));
        GL.GetBoolean(GetPName.DepthWritemask,out bool currentWrites);Assert.Equal(priorWrites,currentWrites);
        Assert.All(glow.ReadPixels(),value=>Assert.Equal(0,value));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        float[] pixels=color.ReadPixels();Assert.All(pixels,value=>Assert.True(float.IsFinite(value)&&value>=0));return pixels;
    }
    /// <summary>Checks independent RGB energy with a one-percent relative tolerance.</summary>
    private static void AssertEnergy(Vector3 expected,Vector3 actual)
    {
        Assert.InRange(Math.Abs(actual.X/expected.X-1),0,.01f);
        Assert.InRange(Math.Abs(actual.Y/expected.Y-1),0,.01f);
        Assert.InRange(Math.Abs(actual.Z/expected.Z-1),0,.01f);
    }
    #endregion
}
