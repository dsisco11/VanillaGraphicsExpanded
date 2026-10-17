using System.Numerics;
using System.Reflection;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes the owned moon shader and integrates incident lux independently of CPU normalization.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class MoonDiskDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Owned moon registrations persist through empty startup, resize, reload and leave-world until explicit disposal.</summary>
    [Fact]
    public void MoonOwnershipRetainsCorrectPassOrderAndLifetime()
    {
        EnsureContextValid();
        Assert.False(MoonDrawPass.IsEnabled);
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using (var renderer = new MoonDrawPass(api.Object, null!))
        {
            Assert.True(MoonDrawPass.IsEnabled);
            renderer.PrepareFrame();
            ScreenResourceManager.HandleScreenResize();
            typeof(MoonDrawPass).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, null);
            Mock.Get(api.Object.Event).Raise(value => value.LeaveWorld += null);
            Assert.True(MoonDrawPass.IsEnabled); Assert.Equal(0, MoonDrawPass.MaximumNits);
        }
        Assert.False(MoonDrawPass.IsEnabled);
    }
    /// <summary>Phase, RGB calibration and frame storage preserve total power through actual rasterization.</summary>
    [Theory]
    [InlineData(4f, 1f, -.1f)]
    [InlineData(2f, .125f, -.1f)]
    [InlineData(1f, 8f, -.1f)]
    [InlineData(6f, 1f, 0f)]
    [InlineData(1f, 1f, 0f)]
    public void RasterizedMoonPreservesIncidentPower(float phase, float scale, float horizon)
    {
        EnsureShaderTestAvailable();
        var lighting = MoonDiskTests.Snapshot(phase) with { HorizonElevation = horizon };
        var disk = MoonDisk.Capture(lighting);
        Vector3 power = Draw(disk, scale);
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(Math.Abs(power[channel] / lighting.Lunar[channel] - 1), 0, .025f);
    }

    /// <summary>Zero lunar power and a fully hidden lunar direction contribute neither scene color nor glow.</summary>
    [Fact]
    public void NewAndHorizonHiddenMoonRemainDark()
    {
        EnsureShaderTestAvailable();
        var lighting = MoonDiskTests.Snapshot(0) with { Lunar = Vector3.Zero };
        Assert.Equal(Vector3.Zero, Draw(MoonDisk.Capture(lighting), 1));
        Assert.Equal(Vector3.Zero, Draw(MoonDisk.Capture(lighting with { HorizonElevation = .2f }), 1));
    }
    #endregion

    #region Private
    /// <summary>Uses a narrow lightweight camera to resolve the disk and analytically integrates pixel solid angles.</summary>
    private Vector3 Draw(MoonDisk disk, float scale)
    {
        const int size = 128;
        float p = 1 / MathF.Tan(.15f / 2);
        Matrix4x4 projection = new(p,0,0,0,0,p,0,0,0,0,-1,-1,0,0,-.2f,0);
        using var camera = TestFrameCamera.CreateFromProjection(projection, size, size);
        using var storage = new TestFrameLightingPublication(); storage.Capture(scale);
        var shader = Programs.Create<MoonProgram>();
        shader.Frame = camera; shader.FrameLightingInputs = storage.Buffer; shader.DrawInputs.Capture(disk);
        using var color = DynamicTexture2D.Create(size, size, PixelInternalFormat.Rgba32f);
        using var glow = DynamicTexture2D.Create(size, size, PixelInternalFormat.Rgba32f);
        using var target = DynamicGpuFramebuffer.CreateMRT([color, glow])!;
        using var geometry = new ArrayGraphicsGeometry(CelestialDrawPass.FullscreenLayout, PrimitiveType.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        using var lifetime = new GraphicsPipelineLifetime();
        var pass = new RenderPassDesc(target, [new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0,0,0,0)),
            new(1, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0,0,0,0))]);
        using var metadata = new RenderPassTargets(pass);
        var blend = new ColorBlendDesc { Enabled = true, SourceRgb = BlendingFactorSrc.One, DestinationRgb = BlendingFactorDest.One };
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, CelestialDrawPass.FullscreenLayout,
            metadata.Signature, DynamicPipelineState.Viewport, blending: [blend, blend]), shader);
        int framebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
        bool depth = GL.IsEnabled(EnableCap.DepthTest), blending = GL.IsEnabled(EnableCap.Blend);
        GL.GetBoolean(GetPName.DepthWritemask, out bool writes);
        Assert.True(GraphicsCommandContext.TryRun("Tests.MoonDisk", [pipeline], true, commands =>
        {
            commands.BeginPass(pass); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0,3)); commands.EndPass();
        }));
        Assert.Equal(framebuffer, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(depth, GL.IsEnabled(EnableCap.DepthTest)); Assert.Equal(blending, GL.IsEnabled(EnableCap.Blend));
        GL.GetBoolean(GetPName.DepthWritemask, out bool currentWrites); Assert.Equal(writes, currentWrites);
        Assert.All(glow.ReadPixels(), value => Assert.Equal(0, value));
        float[] pixels = color.ReadPixels();
        double[] power = new double[3];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double a = (2.0*x/size-1)/p, b = (2.0*(x+1)/size-1)/p;
            double c = (2.0*y/size-1)/p, d = (2.0*(y+1)/size-1)/p;
            double area = CornerArea(b,d)-CornerArea(a,d)-CornerArea(b,c)+CornerArea(a,c);
            double cosine = 1 / Math.Sqrt(1+Math.Pow((a+b)/2,2)+Math.Pow((c+d)/2,2));
            for (int channel = 0; channel < 3; channel++)
            {
                float value = pixels[(y*size+x)*4+channel];
                Assert.True(float.IsFinite(value) && value >= 0 && value <= disk.MaximumNits*scale);
                power[channel] += value / scale * area * cosine;
            }
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        return new((float)power[0], (float)power[1], (float)power[2]);
    }
    /// <summary>Integrates a perspective rectangle in double precision without production geometry helpers.</summary>
    private static double CornerArea(double x, double y) => Math.Atan2(x*y, Math.Sqrt(1+x*x+y*y));
    #endregion
}
