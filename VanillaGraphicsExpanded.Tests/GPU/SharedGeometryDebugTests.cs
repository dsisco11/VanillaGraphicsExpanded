using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises shared diagnostic classification, logical domains and precise surface reconstruction.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SharedGeometryDebugTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the mandatory graphics context.</summary>
    public SharedGeometryDebugTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Diagnostic regressions
    /// <summary>Unknown and unsupported geometry remain distinct from known air, regardless of material readiness.</summary>
    [Theory]
    [InlineData("solid", 1f, 1f, 1f)]
    [InlineData("air", 0f, 0f, 0f)]
    [InlineData("unsupported", 1f, 0f, 0f)]
    [InlineData("unpublished", .5f, 0f, 1f)]
    [InlineData("outside", 0f, .2f, .8f)]
    public void OccupancyClassifiesSharedData(string scenario, float r, float g, float b)
    {
        EnsureShaderTestAvailable();
        var plan = TraceGeometryCoverage.Plan(new(0, 32, 0), true, 32, 256);
        using var scene = new SharedTraceGeometryFixture(plan, new(), (_, _, _) =>
            new(scenario == "air" ? 1u : scenario == "unsupported" ? 3u : 2u, 0, 0));
        if (scenario != "unpublished") scene.Publish();
        AssertColor(Render(scene.Scene, 56, 0, scenario == "outside" ? 20.5f : .5f), r, g, b);
    }

    /// <summary>Bounds are logical surface bounds and packed lighting is read from the shared companion word.</summary>
    [Theory]
    [InlineData(55, .5f, .15f, .85f, .15f)]
    [InlineData(55, 20.5f, .85f, .15f, .15f)]
    [InlineData(57, .5f, 1f, .5f, 1f)]
    public void BoundsAndPayloadUseSharedResources(int mode, float x, float r, float g, float b)
    {
        EnsureShaderTestAvailable();
        var plan = TraceGeometryCoverage.Plan(new(0, 32, 0), true, 32, 256);
        using var scene = new SharedTraceGeometryFixture(plan, new(), (_, _, _) => new(2, 32u | (16u << 6) | (63u << 12), 0));
        scene.Publish();
        AssertColor(Render(scene.Scene, mode, 0, x), r, g, b);
    }
    /// <summary>Compensated camera motion reconstructs the same one-block wall at signed large world origins.</summary>
    [Theory]
    [InlineData(0)] [InlineData(16777216)] [InlineData(-16777216)]
    public void SurfaceClassificationStaysWorldAligned(int anchor)
    {
        EnsureShaderTestAvailable();
        var plan = TraceGeometryCoverage.Plan(new(anchor, 32, 0), true, 32, 256);
        using var scene = new SharedTraceGeometryFixture(plan, new(), (_, y, _) => new(y == 32 ? 2u : 1u, 0, 0));
        scene.Publish();
        foreach (float cameraY in new[] { -.75f, 0f, .75f })
            AssertColor(Render(scene.Scene, 56, anchor, .5f, cameraY), 1, 1, 1);
    }

    /// <summary>The near-field viewer cannot trace the larger physical surface-cache volume.</summary>
    [Fact]
    public void NearViewerClipsToLogicalWindow()
    {
        EnsureShaderTestAvailable();
        var plan = TraceGeometryCoverage.Plan(new(0, 32, 0), true, 64, 256);
        using var scene = new SharedTraceGeometryFixture(plan, new(), (_, _, z) => new(z == -25 ? 6u : 1u, 0, 0));
        scene.Publish();
        AssertColor(Render(scene.Scene, 70, 0, .5f), 0, 0, 0);
        // The same ray must hit that wall when using the larger surface domain.
        float[] surface = Render(scene.Scene, 67, 0, .5f);
        for (int i = 0; i < surface.Length; i += 4) Assert.True(surface[i] > .1f);
    }
    #endregion

    #region Rendering harness
    /// <summary>Binds real shared textures and a deterministic reconstructed surface; camera translation cancels view displacement.</summary>
    private float[] Render(TraceGeometryGpuScene scene, int mode, int anchor, float x, float cameraY = 0)
    {
        int program = CompileShaderWithDefines("lumon_debug.vsh", "lumon_debug.fsh", new()
        { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "1", ["VGE_LUMON_WORLDPROBE_ENABLED"] = "0" });
        try
        {
            // Surface queries land at y=32.5 after the frame bridge; rays face -Z from that same camera.
            float[] projection = [0,0,0,0, 0,0,0,0, 0,0,0,0, x,.5f-cameraY,-5,1];
            float[] view = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,cameraY,0,1];
            var bridge = LumOnFrameWorldSpaceBridge.Compute(anchor, 32, 0);
            UpdateAndBindLumOnFrameUbo(program, invProjectionMatrix: projection, invViewMatrix: view,
                matrixSpaceWorldChunkCoordOffset: bridge.ChunkOffset, matrixSpaceWorldBlockOffsetRem: bridge.BlockOffsetRemainder);
            using var parameters = new ObjectParamsUbo("Tests.SharedGeometryDebug");
            parameters.UploadAndBind(new LumOnDebugParamsUbo { DebugMode = mode }.Bytes);
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnDebugParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            using var localBuffer = GpuUniformBuffer.Create(debugName: "Tests.SharedGeometryDebug.Scene");
            var local = new LumOnNearFieldParamsUbo(); local.SetShared(scene);
            localBuffer.UploadOrResize(local.Bytes, growExponentially: false);
            localBuffer.BindBase(LumOnNearFieldParamsUbo.Binding);
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnNearFieldParamsUbo.BlockName, LumOnNearFieldParamsUbo.Binding);
            using var depth = CreateUniformDepthTexture(ScreenWidth, ScreenHeight, .5f);
            using var normal = CreateUniformNormalTexture(ScreenWidth, ScreenHeight, 0, 0, 1);
            using var patch = Texture2D.Create(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32ui, TextureFilterMode.Nearest);
            patch.UploadDataImmediate(new uint[ScreenWidth * ScreenHeight * 4]);
            depth.Bind(0); normal.Bind(1); patch.Bind(2);
            scene.Geometry.Bind(34); scene.Readiness.Bind(35); scene.Legacy.Bind(20);
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "primaryDepth"), 0);
            GL.Uniform1(GL.GetUniformLocation(program, "gBufferNormal"), 1);
            GL.Uniform1(GL.GetUniformLocation(program, "gBufferPatchId"), 2);
            GL.Uniform1(GL.GetUniformLocation(program, "nearFieldGeometry"), 34);
            GL.Uniform1(GL.GetUniformLocation(program, "nearFieldRegions"), 35);
            GL.Uniform1(GL.GetUniformLocation(program, "traceSceneLegacy"), 20);
            GL.UseProgram(0);
            using var output = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);
            TestFramework.RenderQuadTo(program, output);
            return output[0].ReadPixels();
        }
        finally { GL.DeleteProgram(program); }
    }

    /// <summary>Checks every rendered pixel, including opaque alpha, against the diagnostic legend.</summary>
    private static void AssertColor(float[] pixels, float r, float g, float b)
    {
        float[] expected = [r, g, b, 1];
        for (int i = 0; i < pixels.Length; i++) Assert.InRange(pixels[i], expected[i % 4] - .001f, expected[i % 4] + .001f);
    }
    #endregion
}
