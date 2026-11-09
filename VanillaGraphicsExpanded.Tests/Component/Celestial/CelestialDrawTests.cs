using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
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


using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Celestial;

    /// <summary>Executes production celestial programs with independent footprint and angular-energy references.</summary>
    [Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CelestialDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API


/// <summary>Actual star sampling agrees with admitted atmospheric transport at off-row elevations in clear and cloudy air.</summary>
    [Theory]
    [InlineData(.001f,0f)]
    [InlineData(.001f,.5f)]
    [InlineData(1f,0f)]
    [InlineData(1f,.75f)]
    public void InterpolatedTransmissionMatchesExistingTransport(float altitude,float clouds)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

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
            // Act: sample the production star shader at this off-row elevation.
            Vector3 actual=DrawStar(64,2,centre,1,Vector3.One,false,textures.ExternalTransmission,horizon);
            // Assert: compare each RGB sample with the independent atmospheric transport result.
            for(int c=0;c<3;c++)Assert.InRange(Math.Abs(actual[c]-expected[c]),0,Math.Max(expected[c]*.01f,1e-9f));
        }
        // Assert: a star below the horizon contributes no pixels.
        Assert.Equal(Vector3.Zero,DrawStar(64,2,new(32,16),1,Vector3.One,false,textures.ExternalTransmission,horizon));
    }

/// <summary>Owned rendering follows its independent lifetime through startup, preparation failure, reset and teardown.</summary>
    [Fact]
    public void OwnedRendererRetainsLifetimeThroughFailureAndReset()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        // Act and assert: Owned rendering follows its independent lifetime through startup, preparation failure, reset and teardown.
        Assert.False(CelestialDrawPass.IsEnabled);
        var api=new Mock<ICoreClientAPI>{DefaultValue=DefaultValue.Mock};
        using(var renderer=new CelestialDrawPass(api.Object,null!))
        {
            Assert.True(CelestialDrawPass.IsEnabled);
            renderer.PrepareFrame();
            Assert.True(CelestialDrawPass.IsEnabled);
            ScreenResourceManager.HandleScreenResize();
            Assert.True(CelestialDrawPass.IsEnabled);
            typeof(CelestialDrawPass).GetMethod("Reload",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(renderer,null);
            Assert.True(CelestialDrawPass.IsEnabled);
            Mock.Get(api.Object.Event).Raise(value=>value.LeaveWorld+=null);
            Assert.True(CelestialDrawPass.IsEnabled);
            Assert.Equal(0,CelestialDrawPass.MaximumNits);
        }
        Assert.False(CelestialDrawPass.IsEnabled);
    }

/// <summary>Atmosphere owns transmission storage and uploads dimensionless values without its radiance scale.</summary>
    [Fact]
    public void TransmissionTracksCompletedMediumWithoutPerFrameAllocation()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        var lighting=new AtmosphereLighting(Vector3.UnitY,Vector3.Zero,Vector3.Zero,Vector3.Zero,Vector3.One,ImmutableArray<float>.Empty)
            {Altitude=.001f,HorizonElevation=AtmosphereSkyMapping.Horizon(.001f),CloudCoverage=.25f,Width=1,Height=1,
                Sky=ImmutableArray.Create(0f,0f,0f,1f),ExternalTransmission=AtmosphereExternalTransmission.Build(.001f,2.75f)};
        using var textures=new AtmosphereTextureSet(lighting);
        // Act: upload the completed medium twice, then upload a changed medium into the same owned table.
        textures.Upload(lighting);var first=textures.ExternalTransmission;textures.Upload(lighting);
        // Assert repeated uploads reuse the same table while its samples remain the expected dimensionless transmittance.
        Assert.Same(first,textures.ExternalTransmission);
        using var previous=new GpuImageSnapshot(first);
        var samples=new Dictionary<int,Vector3>();
        foreach(int i in new[]{0,64,255,511})
        {
            float e=AtmosphereSkyMapping.Elevation(i/511f,lighting.HorizonElevation);
            Vector3 expected=AtmosphereModel.ExternalTransmittance(new(MathF.Cos(e),MathF.Sin(e),0),lighting.Altitude,2.75f);
            samples.Add(i,expected);
        }
        ImageAssertions.Comparisons.CompareSelected(first,samples,.000001f).AssertEqual();
        textures.Upload(lighting with{CloudCoverage=.5f,ExternalTransmission=AtmosphereExternalTransmission.Build(.001f,4.5f)});
        Assert.Same(first,textures.ExternalTransmission);
        Assert.True(ImageAssertions.Comparisons.CompareHistory(first,previous).Mismatches>0);
        textures.Dispose();Assert.False(first.IsValid);
    }

/// <summary>Resolution, field of view, off-axis position and subpixel phase preserve calibrated RGB flux.</summary>
    [Theory]
    [InlineData(32, .7f, .5f, .5f)]
    [InlineData(64, 1.4f, .431f, .697f)]
    [InlineData(96, 2f, .9f, .85f)]
    public void IntegratedStarFluxIsCameraIndependent(int size, float fov, float x, float y)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        Vector3 expected = new(.001f,.0005f,.00025f);
        // Act: rasterize one controlled star and integrate it using perspective pixel solid angles.
        Vector3 actual = DrawStar(size,fov,new(size*x,size*y),1,Vector3.One,false);
        // Assert: compare measured RGB flux with the authored incident energy.
        AssertEnergy(expected,actual);
    }

/// <summary>Overlapping instances add linearly, and external extinction attenuates each RGB channel exactly once.</summary>
    [Fact]
    public void OverlapAndTransmissionPreserveLinearEnergy()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        // Act: rasterize three overlapping instances with authored RGB extinction.
        Vector3 actual = DrawStar(64,1,new(31.23f,33.81f),3,new(.5f,.25f,.75f),false);
        // Assert: overlap adds linearly and each extinction channel is applied once.
        AssertEnergy(new(.0015f,.000375f,.0005625f),actual);
    }

/// <summary>A partially visible tent loses its clipped power without renormalization, and rear-facing stars draw nothing.</summary>
    [Fact]
    public void ClippedAndBehindCameraFootprintsAreRejectedConservatively()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        // Act: rasterize a tent clipped by the left edge and a separate rear-facing star.
        Vector3 clipped = DrawStar(32,1,new(-.25f,16.3f),1,Vector3.One,false);
        // Assert: clipping loses the corresponding energy without renormalization, and the rear-facing star draws nothing.
        AssertEnergy(new Vector3(.001f,.0005f,.00025f)*.28125f,clipped);
        Assert.Equal(Vector3.Zero,DrawStar(32,1,new(16,16),1,Vector3.One,true));
    }

/// <summary>The fullscreen program decodes its generated cube once and uses the inverse celestial orientation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GalaxyRotationAndEncodingSelectTheCorrectFace(bool rotated)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        var shader = Programs.Create<CelestialGalaxyProgram>();
        using var camera = TestFrameCamera.CreateFromProjection(Projection(32,.6f),32,32);
        shader.Frame = camera;
        // Arrange: capture authored orientation, horizon and intensity before creating the rendering inputs.
        shader.DrawInputs.Capture(rotated?Matrix4x4.CreateRotationY(MathF.PI/2):Matrix4x4.Identity,.125f,-MathF.PI/2);
        using var cube = RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.Rgba32f, Width = 2, Height = 2, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        for(int face=0;face<6;face++)
        {
            float[] values = Enumerable.Range(0,4).SelectMany(_=>new[]{(float)(face+1),2f,4f,1f}).ToArray();
            using var binding=OpenGlStateCache.Current.BindTextureScope(TextureTarget.TextureCubeMap,0,cube.TextureId);
            GL.TexSubImage2D(TextureTarget.TextureCubeMapPositiveX+face,0,0,0,2,2,PixelFormat.Rgba,PixelType.Float,values);
        }
        using var transmission=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .5f,.25f,.75f,1,.5f,.25f,.75f,1 }).AsSpan()).ToArray())]).RequireResource();
        shader.GalaxyMap=cube;shader.ExternalTransmission=transmission;
        using var geometry=new ArrayGraphicsGeometry(CelestialDrawPass.FullscreenLayout,PrimitiveTopology.Triangles,new Dictionary<int,GpuVbo>(),3);
        // Act: draw the fullscreen galaxy program with the authored cube and transmission textures.
        using var actual=Draw(shader,geometry,CelestialDrawPass.FullscreenLayout,32,1);
        float red=(rotated?1f:6f)*.125f*.5f;
        // Assert: verify the three RGB channels encode the independently selected cube face and transmission.
        ImageAssertions.Statistics.Inspect(actual.Texture,red-.0001f,red+.0001f,channelMask:1).AssertFiniteRange();
        ImageAssertions.Statistics.Inspect(actual.Texture,.06249f,.06251f,channelMask:2).AssertFiniteRange();
        ImageAssertions.Statistics.Inspect(actual.Texture,.37499f,.37501f,channelMask:4).AssertFiniteRange();
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
        using var extinction=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { transmission.X,transmission.Y,transmission.Z,1,transmission.X,transmission.Y,transmission.Z,1 }).AsSpan()).ToArray())]).RequireResource();
        shader.ExternalTransmission=table??extinction;
        Vector3 direction=Vector3.Normalize(new((centre.X/size*2-1)/projection.M11,(centre.Y/size*2-1)/projection.M22,behind?1:-1));
        using var buffer=RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        buffer.UploadData(Enumerable.Range(0,count).SelectMany(_=>new[]{new Vector4(direction,.001f),new Vector4(1,.5f,.25f,0)}).ToArray());
        using var geometry=new ArrayGraphicsGeometry(CelestialStarGeometry.Layout,PrimitiveTopology.Triangles,new Dictionary<int,GpuVbo>{[0]=buffer});
        using var data=Draw(shader,geometry,CelestialStarGeometry.Layout,size,count);
        var weights=new double[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            double lowX=(2.0*x/size-1)/projection.M11,highX=(2.0*(x+1)/size-1)/projection.M11;
            double lowY=(2.0*y/size-1)/projection.M22,highY=(2.0*(y+1)/size-1)/projection.M22;
            double area=CornerArea(highX,highY)-CornerArea(lowX,highY)-CornerArea(highX,lowY)+CornerArea(lowX,lowY);
            weights[y*size+x]=area;
        }
        var integrated=ImageAssertions.Statistics.WeightedStatistics(data.Texture,weights,channelMask:7);
        return new((float)integrated.Sum(0),(float)integrated.Sum(1),(float)integrated.Sum(2));
    }
    /// <summary>Integrates a rectangular perspective pixel analytically in double precision.</summary>
    private static double CornerArea(double x,double y)=>Math.Atan2(x*y,Math.Sqrt(1+x*x+y*y));
    /// <summary>Submits actual prepared graphics pipelines and verifies glow output remains empty.</summary>
    private GpuImageSnapshot Draw(GpuProgram shader,ArrayGraphicsGeometry geometry,RHIVertexLayout layout,int size,int count)
    {
        using var color=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var glow=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var target=DynamicGpuFramebuffer.CreateMRT([color,glow])!;

        var pass=FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(AttachmentIndex:1, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
        var metadata = RHICommandContext.Current.DescribeRenderTargets(pass);
        var blend=new ColorBlendDesc{Enabled=true,SourceRgb=BlendFactor.One,DestinationRgb=BlendFactor.One};
        using var pipeline=RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader,layout,metadata,DynamicPipelineState.Viewport,blending:[blend,blend])).RequireResource();
        int priorFramebuffer=GL.GetInteger(GetPName.DrawFramebufferBinding);
        bool priorDepth=GL.IsEnabled(EnableCap.DepthTest),priorBlend=GL.IsEnabled(EnableCap.Blend);
        GL.GetBoolean(GetPName.DepthWritemask,out bool priorWrites);
        {
    var commands=RHICommandContext.Current;
    using var scope23=commands.EnterBoundary(new("Tests.CelestialDraw",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope23.Run(()=>{
            commands.BeginRenderPass(pass);commands.SetPipeline(pipeline);commands.SetDynamicState(new(){Viewport=commands.PassViewport});
            commands.BindResources(shader.CaptureBindings()); commands.Draw(geometry.Inputs,new(0,layout==CelestialDrawPass.FullscreenLayout?3:6,InstanceCount:count));commands.EndRenderPass();
        });
}
        // Assert: compare the observed result with the expected contract.
        Assert.Equal(priorFramebuffer,GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(priorDepth,GL.IsEnabled(EnableCap.DepthTest));Assert.Equal(priorBlend,GL.IsEnabled(EnableCap.Blend));
        GL.GetBoolean(GetPName.DepthWritemask,out bool currentWrites);Assert.Equal(priorWrites,currentWrites);
        ImageAssertions.Comparisons.CompareConstant(glow, 0).AssertEqual();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        ImageAssertions.Statistics.Inspect(color,0).AssertFiniteRange();return new GpuImageSnapshot(color);
    }
    /// <summary>Checks independent RGB energy with a one-percent relative tolerance.</summary>
    private static void AssertEnergy(Vector3 expected,Vector3 actual)
    {
        Assert.InRange(Math.Abs(actual.X/expected.X-1),0,.01f);
        Assert.InRange(Math.Abs(actual.Y/expected.Y-1),0,.01f);
        Assert.InRange(Math.Abs(actual.Z/expected.Z-1),0,.01f);
    }
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("statistics", 0, 0);
        programs.Prepare("statistics", 0, 3);
    }
    #endregion
}
