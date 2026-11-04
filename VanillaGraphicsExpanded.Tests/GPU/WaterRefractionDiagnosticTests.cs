using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Liquids;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks positive-view-Z production traversal against a controlled opaque receiver field.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterRefractionDiagnosticTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Verifies an oblique submerged receiver is reached by a positive-view-Z Snell ray.</summary>
    [Fact]
    public void PositiveViewZRayCrossesReceiver()
    {
        EnsureContextValid();
        const int size = 128;
        const int budget = 8;
        float receiverDepth = 1.7f;
        float deviceDepth = DeviceDepth(receiverDepth);
        var depths = Enumerable.Repeat(deviceDepth, size * size).ToArray();
        // An oblique submerged plane remains eligible while the bent ray moves toward the camera.
        for (int y = 0; y < size; ++y)
            for (int x = 0; x < size; ++x)
            {
                float rayX = (2f * (x + .5f) / size - 1) / .1f;
                float distance = -6.572f / (-.7f * rayX + .714f);
                depths[y * size + x] = distance > .1f ? DeviceDepth(distance) : 1f;
            }
        using var depth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        var colors = Enumerable.Range(0, size * size).SelectMany(_ => new[] { 4f, 2f, 1f, 1f }).ToArray();
        WaterReceiverTestInputs.EncodeDepthValidity(colors, depths);
        depth.UploadDataImmediate(depths);
        color.UploadDataImmediate(colors);
        var program = Programs.Create<WaterRefractionDiagnosticShaderProgram>();
        var inputs = (IWaterRefractionDiagnosticBindings)program;
        inputs.Scenario = 8;
        const float focal = .1f;
        var projection = new Matrix4x4(focal,0,0,0, 0,focal,0,0, 0,0,-100.1f/99.9f,-1, 0,0,-20f/99.9f,0);
        Assert.True(Matrix4x4.Invert(projection, out var inverse));
        using var frameCamera = TestFrameCamera.CreateFromProjection(projection, size, size);
        program.FrameInputs = frameCamera;
        inputs.Budget = budget;
        inputs.Color = color;
        inputs.Depth = depth;
        using var target = CreateMRTRenderTarget(size, size, TextureFormat.Rgba32f, TextureFormat.Rgba32f,
            TextureFormat.Rgba32f, TextureFormat.Rgba32f, TextureFormat.Rgba32f,
            TextureFormat.Rgba32f, TextureFormat.Rgba32f);
        // A single center pixel isolates the positive-view-Z traversal decision.
        // The render pass still clears every attachment over the full viewport before this bounded draw.
        var scissor = (X: 64, Y: 64, Width: 1, Height: 1);
        int[] viewportBefore = new int[4];
        int[] scissorBefore = new int[4];
        GL.GetInteger(GetPName.Viewport, viewportBefore);
        GL.GetInteger(GetPName.ScissorBox, scissorBefore);
        bool scissorEnabledBefore = GL.IsEnabled(EnableCap.ScissorTest);
        TestFramework.RenderQuadTo(program, target, scissor: scissor);
        int[] viewportAfter = new int[4];
        int[] scissorAfter = new int[4];
        GL.GetInteger(GetPName.Viewport, viewportAfter);
        GL.GetInteger(GetPName.ScissorBox, scissorAfter);
        Assert.Equal(viewportBefore, viewportAfter);
        Assert.Equal(scissorBefore, scissorAfter);
        Assert.Equal(scissorEnabledBefore, GL.IsEnabled(EnableCap.ScissorTest));
        int[] columns = [64];
        foreach (int x in columns)
        {
            float[] decision = target[0].ReadPixelsRegion(x, 64, 1, 1);
            float[] sampled = target[1].ReadPixelsRegion(x, 64, 1, 1);
            float[] transport = target[2].ReadPixelsRegion(x, 64, 1, 1);
            float[] work = target[6].ReadPixelsRegion(x, 64, 1, 1);
            // Published depth alone supplies search eligibility. Only a final
            // accepted triangle may fetch its three associated radiance samples.
            Assert.Equal(0, work[2]);
            Assert.Equal(decision[0], work[3]);
            Assert.Equal(decision[0] * 3, work[1]);
            Assert.InRange(work[1], 0, work[0]);
            Assert.InRange(work[0], 0, decision[2] * 6);
            output.WriteLine($"positive-view-z x={x}: hit/reason/evaluations/confidence=[{string.Join(",", decision)}]; uv/depth=[{string.Join(",", sampled)}]; length/directionZ/fallback/receiverZ=[{string.Join(",", transport)}]");
            Assert.All(decision.Concat(sampled).Concat(transport), value => Assert.True(float.IsFinite(value)));
            Assert.InRange(decision[2], 0, budget);
            if (decision[0] == 1) Assert.Equal(1,decision[3]);
            Assert.True(transport[1] > 0);
            Assert.Equal(1, decision[0]);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Projects an independently specified axial receiver distance into OpenGL device depth.</summary>
    private static float DeviceDepth(float metres) => .5f * (1 + (100.1f - 20 / metres) / 99.9f);

    #endregion
}
