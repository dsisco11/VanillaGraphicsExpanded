using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes the owned solar program against radiometry, metadata and camera-relative ownership contracts.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SunDiskDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    /// <summary>Solar callbacks retain ownership across empty startup, resize, reload and world reset until disposal.</summary>
    [Fact]
    public void SolarOwnershipRetainsPassOrderAndLifetime()
    {
        EnsureContextValid();
        Assert.False(SunDrawPass.IsEnabled);
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using (var renderer = new SunDrawPass(api.Object, null!))
        {
            Assert.True(SunDrawPass.IsEnabled);
            renderer.PrepareFrame();
            ScreenResourceManager.HandleScreenResize();
            typeof(SunDrawPass).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, null);
            Mock.Get(api.Object.Event).Raise(value => value.LeaveWorld += null);
            Assert.True(SunDrawPass.IsEnabled);
        }
        Assert.False(SunDrawPass.IsEnabled);
    }

    /// <summary>The resolved solar disk preserves admitted incident RGB power at multiple storage scales and the horizon.</summary>
    [Theory]
    [InlineData(.001f, -.1f)]
    [InlineData(.01f, -.1f)]
    [InlineData(.001f, 0f)]
    public void RasterizedSolarDiskPreservesIncidentPower(float scale, float horizon)
    {
        EnsureShaderTestAvailable();
        var lighting = Lighting() with { HorizonElevation = horizon };
        using var result = Draw(lighting, scale, 1);
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(Math.Abs(result.Power[channel] / lighting.Solar[channel] - 1), 0, .025f);
        Assert.True(ImageAssertions.Statistics.Inspect(result.Glow,channelMask:2).Maximum>.99f);
        AssertMetadataChannels(result);
    }

    /// <summary>Offscreen, opposite-hemisphere, night and horizon-hidden sources leave both outputs untouched.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void InvisibleSourcesCannotEmitColorOrShaftMetadata(int scenario)
    {
        EnsureShaderTestAvailable();
        var lighting = scenario switch
        {
            0 => Lighting() with { Sun = Vector3.UnitX },
            1 => Lighting() with { Sun = Vector3.UnitZ },
            2 => Lighting() with { Sun = -Vector3.UnitY, Solar = Vector3.Zero },
            3 => Lighting() with { HorizonElevation = .2f, Solar = Vector3.Zero },
            _ => Lighting()
        };
        using var result = Draw(lighting, .001f, 1, validStorage: scenario != 4);
        ImageAssertions.Comparisons.CompareConstant(result.Color,0).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result.Glow,0).AssertEqual();
    }

    /// <summary>The explicit CPU storage limit caps visible radiance before multiplication while preserving RGB ratios.</summary>
    [Fact]
    public void VisibleDiskStorageLimitIsFiniteAndChromatic()
    {
        EnsureShaderTestAvailable();
        var lighting = Lighting() with { Solar = new(100000, 80000, 60000) };
        float maximumScale = AtmosphereSolarDiskStorage.MaximumScale(AtmosphereSolarDisk.Radiance(lighting), 2, 2);
        using var result = Draw(lighting, 8, maximumScale);
        var radiance = AtmosphereSolarDisk.Radiance(lighting) * maximumScale;
        float peak=(float)ImageAssertions.Statistics.Inspect(result.Color,channelMask:1).Maximum;
        Assert.InRange(peak, radiance.X * .999f, radiance.X * 1.001f);
        Assert.InRange(peak, 15999f, 16000f);
        ImageAssertions.Predicates.Predicate(result.Color, AtmospherePredicates.SunDiskDrawTests_VisibleDiskStorageLimitIsFiniteAndChromatic).AssertEqual();
        AssertMetadataChannels(result);
    }

    /// <summary>Moving the camera without rotating it cannot translate a directional celestial emitter.</summary>
    [Fact]
    public void SolarDiskIgnoresCameraTranslation()
    {
        EnsureShaderTestAvailable();
        using var baseline = Draw(Lighting(), .001f, 1);
        float[] translated = [1,0,0,0, 0,1,0,0, 0,0,1,0, 512000,-64000,128000,1];
        using var moved = Draw(Lighting(), .001f, 1, translated);
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(Math.Abs(moved.Power[channel] / baseline.Power[channel] - 1), 0, .001f);
        float peak=(float)ImageAssertions.Statistics.Inspect(baseline.Color).Maximum;
        ImageAssertions.Predicates.Predicate(baseline.Color, AtmospherePredicates.SunDiskDrawTests_SolarDiskIgnoresCameraTranslation,moved.Color,parameters:new(peak*.002f,0,0,0)).AssertEqual();
    }
    #endregion

    #region Private
    /// <summary>Defines a colored solar source fully resolved by the controlled camera.</summary>
    private static AtmosphereLighting Lighting() => new(-Vector3.UnitZ, new(100,80,60),
        Vector3.Zero, Vector3.Zero, Vector3.One, ImmutableArray<float>.Empty) { HorizonElevation = -.1f };

    /// <summary>Keeps the independently measured scene and shaft outputs together with their irradiance integral.</summary>
    private sealed class RasterResult(GpuImageSet images,Vector3 power):IDisposable
    {
        /// <summary>Gets encoded scene radiance retained independently of the temporary framebuffer.</summary>
        internal GpuTexture Color=>images[0].Texture;
        /// <summary>Gets the independent shaft-coverage metadata attachment.</summary>
        internal GpuTexture Glow=>images[1].Texture;
        /// <summary>Gets the independently integrated physical incident power.</summary>
        internal Vector3 Power=>power;
        /// <summary>Releases both retained GPU observations.</summary>
        public void Dispose()=>images.Dispose();
    }

    /// <summary>Asserts additive alpha neutrality and the sole green solar-coverage metadata channel.</summary>
    private void AssertMetadataChannels(RasterResult result)
    {
        ImageAssertions.Statistics.Inspect(result.Color,0).AssertFiniteRange();
        ImageAssertions.Statistics.Inspect(result.Glow).AssertFiniteRange();
        ImageAssertions.Comparisons.CompareConstant(result.Color,0,channelMask:8).AssertEqual();
        ImageAssertions.Statistics.Inspect(result.Glow,0,1,channelMask:2).AssertFiniteRange();
        ImageAssertions.Comparisons.CompareConstant(result.Glow,0,channelMask:13).AssertEqual();
    }
    /// <summary>Renders actual owned solar shaders and integrates independent perspective pixel solid angles.</summary>
    private RasterResult Draw(AtmosphereLighting lighting, float scale, float maximumScale, float[]? view = null, bool validStorage = true)
    {
        const int size = 128;
        double zoom = .75 / Math.Tan(AtmosphereSolarDisk.AngularRadius);
        using var camera = TestFrameCamera.CreateSolar(size, size, view);
        using var storage = new TestFrameLightingPublication(); storage.Capture(scale);
        if (!validStorage) storage.Buffer.UploadData(new[] { scale, 1 / scale, 16000 / scale, 0f, 0f, 0f, 0f, 0f });
        var shader = Programs.Create<SunProgram>();
        shader.Frame = camera; shader.FrameLightingInputs = storage.Buffer;
        shader.DrawInputs.Capture(lighting, maximumScale);
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var glow = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depthImage = new DepthTexture(size, size, TextureFormat.DepthComponent32f);
        using var target = DynamicGpuFramebuffer.CreateMRT([color, glow], depthImage)!;
        using var geometry = new ArrayGraphicsGeometry(CelestialDrawPass.FullscreenLayout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        var pass = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(0,0,0,0)),
            new(AttachmentIndex:1, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(0,0,0,0))], new(DepthLoad: AttachmentLoad.Clear, ClearDepth: .35f));
        var metadata = RHICommandContext.Current.DescribeRenderTargets(pass);
        var blend = new ColorBlendDesc { Enabled = true, SourceRgb = BlendFactor.One, DestinationRgb = BlendFactor.One };
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader, CelestialDrawPass.FullscreenLayout,
            metadata, DynamicPipelineState.Viewport, blending: [blend, blend])).RequireResource();
        bool depth = GL.IsEnabled(EnableCap.DepthTest); GL.GetBoolean(GetPName.DepthWritemask, out bool writes);
        {
    var commands=RHICommandContext.Current;
    using var scope79=commands.EnterBoundary(new("Tests.SunDisk",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope79.Run(()=>{
            commands.BeginRenderPass(pass); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0,3)); commands.EndRenderPass();
        });
}
        Assert.Equal(depth, GL.IsEnabled(EnableCap.DepthTest));
        GL.GetBoolean(GetPName.DepthWritemask, out bool restoredWrites); Assert.Equal(writes, restoredWrites);
        ImageAssertions.Statistics.Inspect(depthImage, .349999f, .350001f).AssertFiniteRange();
        var areas=new double[size*size];var cosines=new double[areas.Length];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            double a=(2.0*x/size-1)/zoom,b=(2.0*(x+1)/size-1)/zoom;
            double c=(2.0*y/size-1)/zoom,d=(2.0*(y+1)/size-1)/zoom;
            areas[y*size+x]=CornerArea(b,d)-CornerArea(a,d)-CornerArea(b,c)+CornerArea(a,c);
            cosines[y*size+x]=1/Math.Sqrt(1+Math.Pow((a+b)/2,2)+Math.Pow((c+d)/2,2));
        }
        var imageProjection = ImageAssertions.Projections;
        using var physical=imageProjection.Apply(color, AtmosphereProjections.MoonDiskDrawTests_Draw,new(scale,0,0,0));
        var power=ImageAssertions.Statistics.WeightedStatistics(physical,areas,channelMask:7,secondaryWeights:cosines);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        return new(new GpuImageSet([color,glow]),new((float)power.Sum(0),(float)power.Sum(1),(float)power.Sum(2)));
    }

    /// <summary>Integrates one perspective rectangle without using production disk helpers.</summary>
    private static double CornerArea(double x, double y) => Math.Atan2(x*y, Math.Sqrt(1+x*x+y*y));
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-atmosphere", 0, 0);
        programs.Prepare("projection-atmosphere", 0, 0);
        programs.Prepare("statistics", 0, 0);
        programs.Prepare("statistics", 0, 3);
    }
    #endregion
}
