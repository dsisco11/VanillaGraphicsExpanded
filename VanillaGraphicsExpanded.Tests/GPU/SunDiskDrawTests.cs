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
[Trait("Category", "GPU")]
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
        var result = Draw(lighting, scale, 1);
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(Math.Abs(result.Power[channel] / lighting.Solar[channel] - 1), 0, .025f);
        Assert.Contains(result.Glow.Where((_, index) => index % 4 == 1), value => value > .99f);
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
        var result = Draw(lighting, .001f, 1, validStorage: scenario != 4);
        Assert.All(result.Color, value => Assert.Equal(0, value));
        Assert.All(result.Glow, value => Assert.Equal(0, value));
    }

    /// <summary>The explicit CPU storage limit caps visible radiance before multiplication while preserving RGB ratios.</summary>
    [Fact]
    public void VisibleDiskStorageLimitIsFiniteAndChromatic()
    {
        EnsureShaderTestAvailable();
        var lighting = Lighting() with { Solar = new(100000, 80000, 60000) };
        float maximumScale = AtmosphereSolarDiskStorage.MaximumScale(AtmosphereSolarDisk.Radiance(lighting), 2, 2);
        var result = Draw(lighting, 8, maximumScale);
        var radiance = AtmosphereSolarDisk.Radiance(lighting) * maximumScale;
        float peak = result.Color.Where((_, index) => index % 4 == 0).Max();
        Assert.InRange(peak, radiance.X * .999f, radiance.X * 1.001f);
        Assert.InRange(peak, 15999f, 16000f);
        for (int pixel = 0; pixel < result.Color.Length; pixel += 4)
        {
            float red = result.Color[pixel];
            Assert.InRange(result.Color[pixel + 1], red * .8f - .02f, red * .8f + .02f);
            Assert.InRange(result.Color[pixel + 2], red * .6f - .02f, red * .6f + .02f);
        }
        AssertMetadataChannels(result);
    }

    /// <summary>Moving the camera without rotating it cannot translate a directional celestial emitter.</summary>
    [Fact]
    public void SolarDiskIgnoresCameraTranslation()
    {
        EnsureShaderTestAvailable();
        var baseline = Draw(Lighting(), .001f, 1);
        float[] translated = [1,0,0,0, 0,1,0,0, 0,0,1,0, 512000,-64000,128000,1];
        var moved = Draw(Lighting(), .001f, 1, translated);
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(Math.Abs(moved.Power[channel] / baseline.Power[channel] - 1), 0, .001f);
        float peak = baseline.Color.Max();
        for (int index = 0; index < baseline.Color.Length; index++)
            Assert.InRange(Math.Abs(baseline.Color[index] - moved.Color[index]), 0, peak * .002f);
    }
    #endregion

    #region Private
    /// <summary>Defines a colored solar source fully resolved by the controlled camera.</summary>
    private static AtmosphereLighting Lighting() => new(-Vector3.UnitZ, new(100,80,60),
        Vector3.Zero, Vector3.Zero, Vector3.One, ImmutableArray<float>.Empty) { HorizonElevation = -.1f };

    /// <summary>Keeps the independently measured scene and shaft outputs together with their irradiance integral.</summary>
    private sealed record RasterResult(float[] Color, float[] Glow, Vector3 Power);

    /// <summary>Asserts additive alpha neutrality and the sole green solar-coverage metadata channel.</summary>
    private static void AssertMetadataChannels(RasterResult result)
    {
        for (int index = 0; index < result.Color.Length; index++)
        {
            Assert.True(float.IsFinite(result.Color[index]) && result.Color[index] >= 0);
            Assert.True(float.IsFinite(result.Glow[index]));
            if (index % 4 == 3) Assert.Equal(0, result.Color[index]);
            if (index % 4 == 1) Assert.InRange(result.Glow[index], 0, 1);
            else Assert.Equal(0, result.Glow[index]);
        }
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
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var glow = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depthImage = new DepthTexture(size, size, PixelInternalFormat.DepthComponent32f);
        using var target = DynamicGpuFramebuffer.CreateMRT([color, glow], depthImage)!;
        using var geometry = new ArrayGraphicsGeometry(CelestialDrawPass.FullscreenLayout, PrimitiveType.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        using var lifetime = new GraphicsPipelineLifetime();
        var pass = new RenderPassDesc(target, [new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0,0,0,0)),
            new(1, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0,0,0,0))],
            new(DepthLoad: AttachmentLoad.Clear, ClearDepth: .35f));
        using var metadata = new RenderPassTargets(pass);
        var blend = new ColorBlendDesc { Enabled = true, SourceRgb = BlendingFactorSrc.One, DestinationRgb = BlendingFactorDest.One };
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, CelestialDrawPass.FullscreenLayout,
            metadata.Signature, DynamicPipelineState.Viewport, blending: [blend, blend]), shader);
        bool depth = GL.IsEnabled(EnableCap.DepthTest); GL.GetBoolean(GetPName.DepthWritemask, out bool writes);
        Assert.True(GraphicsCommandContext.TryRun("Tests.SunDisk", [pipeline], true, commands =>
        {
            commands.BeginPass(pass); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0,3)); commands.EndPass();
        }));
        Assert.Equal(depth, GL.IsEnabled(EnableCap.DepthTest));
        GL.GetBoolean(GetPName.DepthWritemask, out bool restoredWrites); Assert.Equal(writes, restoredWrites);
        Assert.All(depthImage.ReadPixels(), value => Assert.InRange(value, .349999f, .350001f));
        float[] pixels = color.ReadPixels();
        double[] power = new double[3];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double a = (2.0*x/size-1)/zoom, b = (2.0*(x+1)/size-1)/zoom;
            double c = (2.0*y/size-1)/zoom, d = (2.0*(y+1)/size-1)/zoom;
            double area = CornerArea(b,d)-CornerArea(a,d)-CornerArea(b,c)+CornerArea(a,c);
            double cosine = 1 / Math.Sqrt(1+Math.Pow((a+b)/2,2)+Math.Pow((c+d)/2,2));
            for (int channel = 0; channel < 3; channel++) power[channel] += pixels[(y*size+x)*4+channel] / scale * area * cosine;
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        return new(pixels, glow.ReadPixels(), new((float)power[0], (float)power[1], (float)power[2]));
    }

    /// <summary>Integrates one perspective rectangle without using production disk helpers.</summary>
    private static double CornerArea(double x, double y) => Math.Atan2(x*y, Math.Sqrt(1+x*x+y*y));
    #endregion
}
