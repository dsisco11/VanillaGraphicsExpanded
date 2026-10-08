using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises production liquid refraction with immutable scene radiance and depth.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    private const int LavaCase = 1;
    private const int FullAlphaCase = 2;
    private const int FogCase = 3;
    private const int ShadowCase = 4;
    private const int FlowCase = 5;
    #region Public API
    /// <summary>Accepted opaque hits replace OIT transmission while invalid sources retain straight-through behavior.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(9, true)]
    [InlineData(14, true)]
    [InlineData(9, true, 1)]
    [InlineData(9, true, 2)]
    [InlineData(0, true, 0, 2)]
    [InlineData(1, true, 0, 2)]
    [InlineData(11, true, 0, 2)]
    [InlineData(14, true, 0, 2)]
    [InlineData(0, false, 0, 1, 0)]
    [InlineData(0, true, 0, 1, 0)]
    [InlineData(0, true, 0, 2, 0)]
    [InlineData(1, true, 0, 1, 0)]
    [InlineData(1, true, 0, 2, 0)]
    [InlineData(3, true, 0, 1, 0)]
    [InlineData(6, true, 0, 1, 0)]
    [InlineData(7, true, 0, 1, 0)]
    [InlineData(10, true, 0, 1, 0)]
    [InlineData(11, true, 0, 2, 0)]
    [InlineData(9, false, 0, 1, 0)]
    [InlineData(9, true, 0, 1, 0)]
    [InlineData(9, true, 1, 1, 0)]
    [InlineData(9, true, 2, 1, 0)]
    [InlineData(0, false, 0, 1, 1)]
    [InlineData(0, true, 0, 2, 1)]
    [InlineData(1, true, 0, 2, 1)]
    [InlineData(6, true, 0, 1, 1)]
    [InlineData(7, true, 0, 1, 1)]
    [InlineData(8, true, 0, 1, 1)]
    [InlineData(9, false, 0, 1, 1)]
    [InlineData(9, true, 1, 1, 1)]
    [InlineData(9, true, 2, 1, 1)]
    [InlineData(11, true, 0, 2, 1)]
    [InlineData(14, true, 0, 2, 1)]
    [InlineData(0, false, 0, 1, 2)]
    [InlineData(0, true, 0, 2, 2)]
    [InlineData(1, true, 0, 2, 2)]
    [InlineData(6, true, 0, 1, 2)]
    [InlineData(7, true, 0, 1, 2)]
    [InlineData(8, true, 0, 1, 2)]
    [InlineData(9, false, 0, 1, 2)]
    [InlineData(9, true, 1, 1, 2)]
    [InlineData(9, true, 2, 1, 2)]
    [InlineData(11, true, 0, 2, 2)]
    [InlineData(14, true, 0, 2, 2)]
    [InlineData(0, true, 0, 1, 1)]
    [InlineData(0, true, 0, 1, 2)]
    [InlineData(3, true, 0, 2, 0)]
    [InlineData(3, true, 0, 1, 1)]
    [InlineData(3, true, 0, 2, 1)]
    [InlineData(3, true, 0, 1, 2)]
    [InlineData(3, true, 0, 2, 2)]
    [InlineData(3, true, 0, 1, 3)]
    [InlineData(3, true, 0, 2, 3)]
    [InlineData(6, true, 0, 2, 0)]
    [InlineData(6, true, 0, 2, 1)]
    [InlineData(6, true, 0, 2, 2)]
    [InlineData(6, true, 0, 1, 3)]
    [InlineData(6, true, 0, 2, 3)]
    [InlineData(7, true, 0, 2, 0)]
    [InlineData(7, true, 0, 2, 1)]
    [InlineData(7, true, 0, 2, 2)]
    [InlineData(7, true, 0, 2, 3)]
    [InlineData(0, false, 0, 2, 0)]
    [InlineData(0, false, 0, 2, 1)]
    [InlineData(0, false, 0, 2, 2)]
    [InlineData(0, false, 0, 2, 3)]
    [InlineData(0, true, 0, 1, 3, LavaCase)]
    [InlineData(0, true, 0, 1, 3, FullAlphaCase)]
    [InlineData(0, true, 0, 1, 3, FogCase)]
    [InlineData(9, true, 1, 1, 3, ShadowCase)]
    [InlineData(0, true, 0, 1, 3, FlowCase)]
    [InlineData(0, true, 0, 1, 0, 0, true)]
    [InlineData(0, true, 0, 2, 0, 0, true)]
    public void OpaqueHitsAndUnavailableSourcesHaveDefinedComposition(int scenario, bool sceneLinear = false, int scatteringSource = 0, int backgroundScale = 1, int refractionQuality = 3, int compatibility = 0, bool captureReceiver = false)
    {
        EnsureContextValid();
        string? binaryDirectory = Environment.GetEnvironmentVariable("VGE_WATER_SURFACE_BINARIES");
        bool measure = Environment.GetEnvironmentVariable("VGE_MEASURE_WATER_SURFACE") == "1" && scenario == 0 && compatibility == 0;
        bool phaseMeasurement = measure && Environment.GetEnvironmentVariable("VGE_MEASURE_WATER_PHASE") == "1";
        int frameSize = measure ? 512 : scenario >= 14 ? 128 : 16;
        int center = frameSize / 2;
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        if (!string.IsNullOrEmpty(binaryDirectory))
        {
            assets.BeforeRead = path =>
            {
                bool variant = path.Contains("variants/pbr_liquid.fsh/", StringComparison.Ordinal);
                if (!variant && !path.EndsWith("/pbr_liquid.fsh.spv", StringComparison.Ordinal)) return;
                string binary = Path.Combine(binaryDirectory, variant
                    ? $"pbr_liquid.fsh.{Path.GetFileNameWithoutExtension(path)}.glsl.spv" : "default.spv");
                assets.Overrides[path] = File.ReadAllBytes(binary);
            };
        }
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        program.ConfigureOptions(() =>
        {
            program.RefractionQuality = refractionQuality;
            program.RefractionBackgroundScale = 3 - backgroundScale;
        });
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        if (!string.IsNullOrEmpty(binaryDirectory)) Assert.NotEmpty(assets.Overrides);
        program.SceneLinear = sceneLinear;
        using var target = CreateMRTRenderTarget(frameSize, frameSize, PixelInternalFormat.Rgb32f, PixelInternalFormat.R32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var terrain = DynamicTexture2D.Create(compatibility == FlowCase ? 8 : 1, compatibility == FlowCase ? 8 : 1, PixelInternalFormat.Rgba32f);
        using var material = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(frameSize, frameSize, PixelInternalFormat.R32f);
        using var aerial = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        // Allocation contents are undefined; numerical optics require an explicitly empty atmosphere.
        aerial.UploadDataImmediate(new float[4], 0, 0, 0, 1, 1, 1, 0);
        if (compatibility == FlowCase)
            terrain.UploadDataImmediate(Enumerable.Range(0, 64).SelectMany(pixel => new float[] { .1f * (pixel % 8 + 1), .2f, .3f, 1 }).ToArray());
        else terrain.UploadDataImmediate(new float[] { 1, 1, 1, 1 });
        material.UploadDataImmediate([.1f, 0, 0, 1]);
        if (compatibility is LavaCase or FullAlphaCase or FlowCase)
            material.UploadDataImmediate([.1f, 0, 1, 1]);
        const float near = .1f, far = 100;
        float receiver = scenario == 2 ? 1 : scenario == 5 ? 80 : 10;
        float deviceDepth = .5f * (1 + (far + near - 2 * far * near / receiver) / (far - near));
        depth.UploadDataImmediate(Enumerable.Repeat(deviceDepth, frameSize * frameSize).ToArray());
        using var sceneColor = DynamicTexture2D.Create(frameSize, frameSize, PixelInternalFormat.Rgba32f);
        using var sceneDepth = DynamicTexture2D.Create(frameSize, frameSize, PixelInternalFormat.R32f);
        var sceneColors = Enumerable.Range(0, frameSize * frameSize).SelectMany(pixel => scenario == 9
            ? new float[] { 1f + (pixel % 16) * .2f, .2f, .1f, 1f }
            : new float[] { 4f, 2f, 1f, scenario == 3 ? 0f : 1f }).ToArray();
        sceneColor.UploadDataImmediate(sceneColors);
        var sceneDepthValues = Enumerable.Repeat(scenario == 4 ? float.NaN : deviceDepth, frameSize * frameSize).ToArray();
        if (scenario == 10) sceneDepthValues[8 * 16 + 9] = .5f * (1 + (far + near - 2 * far * near) / (far - near));
        WaterReceiverTestInputs.EncodeDepthValidity(sceneColors, sceneDepthValues);
        sceneDepth.UploadDataImmediate(sceneDepthValues);
        if (scatteringSource != 0)
            sceneColor.UploadDataImmediate(Enumerable.Range(0, frameSize * frameSize).SelectMany(_ => new float[] { 0, 0, 0, 1 }).ToArray());
        using var mediumIndex = Texture2D.Create(1, 1, PixelInternalFormat.R32f);
        using var mediumRecord = Texture2D.Create(2, 1, PixelInternalFormat.Rgba32f);
        mediumIndex.UploadDataImmediate([1f]);
        mediumRecord.UploadDataImmediate(new float[8]);
        if (scatteringSource != 0) mediumRecord.UploadDataImmediate([.1f, .2f, .3f, .7f, .2f, .3f, .4f, 0]);
        program.WaterMediumIndicesTexture = mediumIndex;
        program.WaterMediumRecordsTexture = mediumRecord;
        program.RefractionColorTexture = sceneColor;
        program.RefractionDepthTexture = sceneDepth;
        program.TerrainTexture = terrain.TextureId;
        program.MaterialParamsTexture = material;
        program.DepthTexture = depth.TextureId;
        program.AerialRadianceTexture = aerial;
        program.AerialAttenuationTexture = aerial;
        program.ShadowRanges = Vector4.Zero;
        program.SunDirection = new(Vector3.UnitY, 0);
        program.SolarIrradiance = Vector4.Zero;
        program.EnvironmentIrradiance = Vector4.Zero;
        program.DepthRangeAndFrameSize = new(near, far, frameSize, frameSize);
        program.AtlasMetrics = Vector4.One;
        program.SetCounts(0, 0);
        if (compatibility == FogCase)
        {
            // A camera-centred sphere covers the whole visible segment with saturated fog.
            program.SetCounts(0, 1);
            float[] sphere = [0, 0, 0, 10, 1, .5f, .25f, .125f];
            for (int index = 0; index < sphere.Length; index++) program.SetFogSphereComponent(index, sphere[index]);
        }
        using var shadow = compatibility == ShadowCase ? new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f) : null;
        if (shadow is not null)
        {
            // Constant cascade coordinates and depth zero establish complete solar occlusion.
            shadow.UploadDataImmediate(new float[] { 0 });
            program.ShadowMapNear = shadow.TextureId;
            program.ShadowMapFar = shadow.TextureId;
            program.ShadowRanges = new(100, 100, 0, 0);
            float[] matrix = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, .5f, .5f, .5f, 1];
            program.ShadowMatrixNear = matrix;
            program.ShadowMatrixFar = matrix;
        }
        if (compatibility == FlowCase) program.AtlasMetrics = new(1, 1, 8, 8);
        if (scatteringSource == 1)
        {
            program.SunDirection = new(0, 0, -1, 0);
            program.SolarIrradiance = new(100, 100, 100, 0);
        }
        if (scatteringSource == 2)
        {
            program.SetCounts(1, 0);
            program.SetPointLightPosition(0, new(0, 0, -12));
            program.SetPointLightColor(0, new(10000));
        }
        program.MediumLookupEnabled = scenario == 9 || scenario >= 14;
        program.AerialParameters = new(0, 0, scenario is >= 6 and <= 8 ? 1 : 0, 0);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1, near, far);
        projection.M33 = -(far + near) / (far - near);
        projection.M43 = -2 * far * near / (far - near);
        program.ProjectionMatrix = Flatten(projection);
        Matrix4x4 modelView = scenario switch
        {
            11 => Matrix4x4.CreateRotationY(1.2f),
            12 => Matrix4x4.CreateRotationX(.9f),
            13 => Matrix4x4.CreateRotationZ(.7f) * Matrix4x4.CreateRotationY(-.8f),
            _ => Matrix4x4.Identity
        };
        Matrix4x4.Invert(modelView, out Matrix4x4 worldFromView);
        Vector3 camera = new(0, 2, 0);
        if (scenario >= 14)
        {
            float pitch = new[] { 15, 45, 75 }[(scenario - 14) % 3] * MathF.PI / 180;
            float yaw = scenario >= 17 ? MathF.PI / 2 : 0;
            Vector3 forward = new(MathF.Sin(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
            modelView = Matrix4x4.CreateLookAt(camera, camera + forward, Vector3.UnitY);
            Matrix4x4.Invert(modelView, out worldFromView);
            var floorDepth = new float[frameSize * frameSize];
            var floorColor = new float[frameSize * frameSize * 4];
            // Generate the opaque floor independently from camera rays, keeping its world position fixed.
            for (int pixel = 0; pixel < frameSize * frameSize; ++pixel)
            {
                Vector3 ray = Vector3.TransformNormal(new((2f * (pixel % frameSize + .5f) / frameSize - 1) / MathF.Sqrt(3),
                    (2f * (pixel / frameSize + .5f) / frameSize - 1) / MathF.Sqrt(3), -1), worldFromView);
                if (ray.Y >= 0)
                {
                    floorDepth[pixel] = 1;
                    continue;
                }
                Vector3 floor = camera + ray * (-5 / ray.Y);
                Vector4 clip = Vector4.Transform(new Vector4(floor, 1), modelView * projection);
                floorDepth[pixel] = (clip.Z / clip.W + 1) * .5f;
                floorColor[pixel * 4] = MathF.Max(.1f, 2 + floor.X * .02f);
                floorColor[pixel * 4 + 1] = .2f;
                floorColor[pixel * 4 + 2] = MathF.Max(.1f, 1 + floor.Z * .02f);
                floorColor[pixel * 4 + 3] = 1;
            }
            depth.UploadDataImmediate(floorDepth);
            sceneDepth.UploadDataImmediate(floorDepth);
            sceneColor.UploadDataImmediate(floorColor);
        }
        program.ModelViewMatrix = Flatten(modelView);
        // Feed the production traversal an actual geometry-aware reduction of the
        // same receiver scene; projection/frame dimensions remain full resolution.
        using var reducedColor = backgroundScale == 2 ? DynamicTexture2D.Create((frameSize + 1) / 2, (frameSize + 1) / 2, PixelInternalFormat.Rgba32f) : null;
        using var reducedDepth = backgroundScale == 2 ? DynamicTexture2D.Create((frameSize + 1) / 2, (frameSize + 1) / 2, PixelInternalFormat.Rgba32f) : null;
        using var reductionTarget = backgroundScale == 2 ? GpuFramebuffer.CreateMRT([reducedColor!, reducedDepth!]) : null;
        if (backgroundScale == 2)
        {
            var reduction = GpuShaderPrograms.Declare(assets.Api, new WaterRefractionReductionShaderProgram());
            reduction.SourceColor = sceneColor; reduction.SourceDepth = sceneDepth;
            using var drawing = new VanillaGraphicsExpanded.Tests.GPU.Helpers.ShaderTestFramework();
            drawing.RenderQuadTo(reduction, reductionTarget!);
            program.RefractionColorTexture = reducedColor;
            program.RefractionDepthTexture = reducedDepth;
        }
        // Consume the actual composite publication directly, including its reduced pair.
        using var capturedReceiver = captureReceiver ? new RuntimeWaterReceiver(assets, frameSize,
            Flatten(projection), deviceDepth, backgroundScale) : null;
        if (capturedReceiver is not null)
        {
            Assert.True(capturedReceiver.Scene.Published);
            program.RefractionColorTexture = capturedReceiver.Scene.Color;
            program.RefractionDepthTexture = capturedReceiver.Scene.Depth;
            Assert.True(capturedReceiver.Scene.Color!.ReadPixels().Max() > 1);
        }
        var state = StateCache.Current;
        using var fixedFunction = LegacyFixedFunctionReference.Capture(state);
        using var framebuffer = state.BindFramebufferScope(FramebufferTarget.Framebuffer, target.FboId);
        target.BindWithViewport();

        state.Apply(new GlPipelineDesc(defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
            .With(GlPipelineStateId.CullFaceEnable).With(GlPipelineStateId.ScissorTestEnable).With(GlPipelineStateId.ColorMask)
            .With(GlPipelineStateId.BlendEnable), nonDefaultMask: default));
        using var vao = GpuVao.Create("Test.WaterCapture");
        using var vertices = GpuVbo.Create(debugName: "Test.WaterCapture.Vertices");
        using var flags = GpuVbo.Create(debugName: "Test.WaterCapture.Flags");
        using var binding = vao.BindScope();
        using var indices = GpuEbo.Create(debugName: "Test.WaterCapture.Indices");
        indices.UploadIndices(new uint[] { 0, 1, 2, 3, 4, 5 });
        using (vertices.BindScope())
        {
            vertices.UploadData(new float[6 * 11]);
            vao.AttribPointer(0, 3, VertexAttribPointerType.Float, false, 44, 0);
            vao.AttribPointer(1, 2, VertexAttribPointerType.Float, false, 44, 12);
            vao.AttribPointer(2, 4, VertexAttribPointerType.Float, false, 44, 20);
            vao.AttribPointer(4, 2, VertexAttribPointerType.Float, false, 44, 36);
        }
        using (flags.BindScope())
        {
            flags.UploadData(new int[18]);
            vao.AttribIPointer(3, 1, VertexAttribIntegerType.Int, 12, 0);
            vao.AttribIPointer(5, 1, VertexAttribIntegerType.Int, 12, 4);
            vao.AttribIPointer(6, 1, VertexAttribIntegerType.Int, 12, 8);
        }
        float validationSky = 1;
        program.RefractionEnabled = false;
        target.Clear(0, 0, 0, 0);
        DrawBoundary(2, true);
        var baseline = target[1].ReadPixelsRegion(center, center, 1, 1);
        var baselineAccumulation = target[3].ReadPixelsRegion(center, center, 1, 1);
        var edgeBaseline = target[1].ReadPixelsRegion(8, 0, 1, 8);
        if (compatibility == FlowCase) program.Animation = new(0, 1, 0, 0);
        program.RefractionEnabled = true;
        Assert.Equal(1f, BitConverter.ToSingle(((ILiquidShaderProgramBindings)program).FrameParameters.Bytes.Slice(4632, 4)));
        target.Clear(0, 0, 0, 0);
        DrawBoundary(2, true);
        var actual = target[1].ReadPixelsRegion(center, center, 1, 1);
        Assert.All(actual, value => Assert.True(float.IsFinite(value)));
        if (compatibility is LavaCase or FullAlphaCase)
        {
            // Excluded liquid materials preserve their original body response despite valid receiver data.
            Assert.InRange(MathF.Abs(actual[0] - baseline[0]), 0, .00001f);
            Assert.InRange(Vector4.Distance(new(target[3].ReadPixelsRegion(center, center, 1, 1)), new(baselineAccumulation)), 0, .00001f);
            Assert.True(baselineAccumulation[0] > 0);
        }
        else if (compatibility == FlowCase)
        {
            var accumulation = target[3].ReadPixelsRegion(center, center, 1, 1);
            Assert.InRange(MathF.Abs(baselineAccumulation[0] / baselineAccumulation[3] - Decode(.4f)), 0, .0001f);
            Assert.InRange(MathF.Abs(accumulation[0] / accumulation[3] - Decode(.6f)), 0, .0001f);
            Assert.InRange(actual[0], 0, .00001f);
        }
        else if (compatibility == FogCase)
        {
            var accumulation = target[3].ReadPixelsRegion(center, center, 1, 1);
            float[] fog = [.5f, .25f, .125f];
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(MathF.Abs(accumulation[channel] / accumulation[3] - Decode(fog[channel])), 0, .0001f);
            Assert.InRange(actual[0], 0, .00001f);
        }
        else if (compatibility == ShadowCase)
        {
            var accumulation = target[3].ReadPixelsRegion(center, center, 1, 1);
            Assert.All(accumulation.Take(3), value => Assert.InRange(MathF.Abs(value), 0, .00001f));
            // Restore sunlight and verify its independent Beer-Lambert/Henyey-Greenstein prediction.
            shadow!.UploadDataImmediate(new float[] { 1 });
            target.Clear(0, 0, 0, 0);
            DrawBoundary(2, true);
            AssertReceiverGradient(target[3].ReadPixelsRegion(center, center, 1, 1), true, 1, refractionQuality);
        }
        else if (scenario >= 14)
        {
            string receiverDecision = "";
            if (actual[0] >= baseline[0])
            {
                // Replay the same center ray through the production diagnostic binary;
                // this reports rejection without replacing the liquid integration assertion.
                var diagnostic = GpuShaderPrograms.Declare(assets.Api, new WaterRefractionDiagnosticShaderProgram());
                var diagnosticInputs = (IWaterRefractionDiagnosticBindings)diagnostic;
                diagnosticInputs.Scenario = 12;
                Assert.True(Matrix4x4.Invert(projection, out var inverse));
                diagnosticInputs.Projection = projection; diagnosticInputs.InverseProjection = inverse;
                diagnosticInputs.Budget = refractionQuality == 1 ? 2 : refractionQuality == 2 ? 4 : 8;
                diagnosticInputs.FrameSize = new(frameSize);
                Vector3 viewRay = new((2f * (center + .5f) / frameSize - 1) / MathF.Sqrt(3),
                    (2f * (center + .5f) / frameSize - 1) / MathF.Sqrt(3), -1);
                Vector3 worldRay = Vector3.TransformNormal(viewRay, worldFromView);
                diagnosticInputs.Surface = viewRay * (-camera.Y / worldRay.Y);
                diagnosticInputs.Normal = Vector3.TransformNormal(Vector3.UnitY, modelView);
                diagnosticInputs.Color = reducedColor ?? sceneColor;
                diagnosticInputs.Depth = reducedDepth ?? sceneDepth;
                using var diagnosticTarget = CreateMRTRenderTarget(frameSize, frameSize,
                    PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
                using var diagnosticDraw = new VanillaGraphicsExpanded.Tests.GPU.Helpers.ShaderTestFramework();
                diagnosticDraw.RenderQuadTo(diagnostic, diagnosticTarget);
                receiverDecision = $" decision={string.Join(",", diagnosticTarget[0].ReadPixelsRegion(center, center, 1, 1))} sample={string.Join(",", diagnosticTarget[1].ReadPixelsRegion(center, center, 1, 1))}";
            }
            Assert.True(actual[0] < baseline[0], $"Fixed-world camera scenario {scenario} must transmit a refracted floor sample.{receiverDecision}");
            AssertFixedWorldSnellGradient(target[3].ReadPixelsRegion(center, center, 1, 1), baselineAccumulation,
                actual[0], baseline[0], frameSize, camera, worldFromView, modelView * projection, sceneLinear);
        }
        else if (scenario == 5)
        {
            // Geometric coverage beyond the traversal extent still has a valid
            // approximate UV receiver; optical attenuation may remove its red channel.
            Assert.InRange(actual[0], 0, .00001f);
            Assert.True(baseline[0] > .001f);
        }
        else if (scenario < 2 || scenario == 6 || scenario == 8 || scenario == 9 || scenario == 10 || scenario >= 11)
        {
            // The steep underwater exit also retains a valid receiver.
            // Zero revealage proves scene radiance replaces rather than re-blends the original background.
            Assert.InRange(actual[0], 0, .00001f);
            var accumulation = target[3].ReadPixelsRegion(center, center, 1, 1);
            Assert.True(accumulation[0] > 0);
            Assert.True(baseline[0] > .001f);
            if (scenario == 9) AssertReceiverGradient(accumulation, sceneLinear, scatteringSource, refractionQuality);
            if (sceneLinear && scenario is 0 or 9) Assert.True(accumulation.Take(3).Max() / accumulation[3] > 1);

        }
        else if (scenario == 7)
        {
            Assert.InRange(actual[0], 0, .00001f);
            var accumulated = target[3].ReadPixelsRegion(center, center, 1, 1);
            Assert.All(accumulated.Take(3), value => Assert.InRange(MathF.Abs(value), 0, .00001f));
        }
        else
            for (int channel = 0; channel < actual.Length; channel++)
                Assert.InRange(MathF.Abs(actual[channel] - baseline[channel]), 0, .00001f);
        if (scenario == 0 && compatibility == 0)
        {
            // Supported edge receivers retain transmission instead of fading it by position.
            var edgeRevealage = target[1].ReadPixelsRegion(8, 0, 1, 8);
            for (int row = 0; row < 8; ++row) Assert.InRange(edgeRevealage[row], 0, .00001f);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        if (!string.IsNullOrEmpty(binaryDirectory))
        {
            // Compare the same existing numerical scenarios across independently
            // compiled binaries without changing their expected optical outcomes.
            output.WriteLine($"surface-output scenario={scenario} quality={refractionQuality} scale={backgroundScale} compatibility={compatibility} disabledReveal={string.Join(',', baseline)} disabledAccumulation={string.Join(',', baselineAccumulation)}");
            for (int attachment = 0; attachment < 6; attachment++)
                output.WriteLine($"surface-mrt index={attachment} center={string.Join(',', target[attachment].ReadPixelsRegion(center, center, 1, 1))}");
        }
        if (Environment.GetEnvironmentVariable("VGE_VALIDATE_WATER_AERIAL") == "1")
        {
            // The normal scenario assertions run first. This optional comparison
            // then records actual production outputs with nonzero atmospheric LUTs.
            program.AerialParameters = new(100, 100, scenario is >= 6 and <= 8 ? 1 : 0, 0);
            aerial.UploadDataImmediate([.05f, .1f, .2f, .1f], 0, 0, 0, 1, 1, 1, 0);
            foreach (float sky in new[] { 0f, .4f, 1f })
                foreach (bool enabled in new[] { false, true })
                {
                    validationSky = sky; program.RefractionEnabled = enabled;
                    target.Clear(0, 0, 0, 0); DrawBoundary(2, true);
                    for (int attachment = 0; attachment < 6; attachment++)
                    {
                        float[] values = target[attachment].ReadPixelsRegion(center, center, 1, 1);
                        Assert.All(values, value => Assert.True(float.IsFinite(value)));
                        output.WriteLine($"aerial-output scenario={scenario} quality={refractionQuality} source={scatteringSource} compatibility={compatibility} sky={sky:R} enabled={enabled} mrt={attachment} values={string.Join(',', values.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}");
                    }
                }
        }
        if (Environment.GetEnvironmentVariable("VGE_VALIDATE_WATER_PHASE") == "1")
        {
            // Matched production binaries exercise independent solar and point
            // sources through both fallback and selected receiver composition.
            program.MediumLookupEnabled = true;
            program.EnvironmentIrradiance = Vector4.Zero;
            foreach (int light in new[] { 1, 2 })
                foreach (float anisotropy in new[] { 0f, .7f, -.7f, 1e-7f, -1e-7f, 2f, -2f })
                    foreach (bool enabled in new[] { false, true })
                    {
                        mediumRecord.UploadDataImmediate([.1f, .2f, .3f, anisotropy, .2f, .3f, .4f, 0]);
                        program.SunDirection = new(0, 0, -1, 0);
                        program.SolarIrradiance = light == 1 ? new(100, 100, 100, 0) : Vector4.Zero;
                        program.SetCounts(light == 2 ? 1 : 0, 0);
                        program.SetPointLightPosition(0, new(0, 0, -12));
                        program.SetPointLightColor(0, new(10000));
                        program.RefractionEnabled = enabled;
                        target.Clear(0, 0, 0, 0); DrawBoundary(2, true);
                        for (int attachment = 0; attachment < 6; attachment++)
                        {
                            float[] values = target[attachment].ReadPixelsRegion(center, center, 1, 1);
                            Assert.All(values, value => Assert.True(float.IsFinite(value)));
                            output.WriteLine($"phase-output scenario={scenario} quality={refractionQuality} light={light} g={anisotropy:R} enabled={enabled} mrt={attachment} values={string.Join(',', values.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}");
                        }
                    }
        }
        if (measure)
        {
            Assert.Equal(1, backgroundScale);
            output.WriteLine($"surface-device renderer={GL.GetString(StringName.Renderer)} version={GL.GetString(StringName.Version)} binaries={binaryDirectory}");
            // Keep transport, source and shared point-light reflection active.
            // Receiver eligibility alone varies between coherent and mixed regions.
            mediumRecord.UploadDataImmediate([.1f, .2f, .3f, .7f, .2f, .3f, .4f, 0]);
            using var phaseIndices = phaseMeasurement ? Texture2D.Create(frameSize, frameSize, PixelInternalFormat.R32f) : null;
            using var phaseRecords = phaseMeasurement ? Texture2D.Create(4, 1, PixelInternalFormat.Rgba32f) : null;
            if (phaseMeasurement)
            {
                phaseRecords!.UploadDataImmediate([.1f, .2f, .3f, 0, .2f, .3f, .4f, 0, .1f, .2f, .3f, .7f, .2f, .3f, .4f, 0]);
                program.WaterMediumIndicesTexture = phaseIndices!; program.WaterMediumRecordsTexture = phaseRecords;
            }
            program.MediumLookupEnabled = true;
            program.SolarIrradiance = new(10, 10, 10, 0);
            program.EnvironmentIrradiance = new(2, 2, 2, 0);
            program.SetCounts(1, 0);
            program.SetPointLightPosition(0, new(0, 0, -12)); program.SetPointLightColor(0, new(100));
            program.AerialParameters = new(100, 100, 0, 0);
            aerial.UploadDataImmediate([.05f, .05f, .05f, .1f], 0, 0, 0, 1, 1, 1, 0);
            float[]? isotropicPixels = null, anisotropicPixels = null;
            foreach (string workload in phaseMeasurement ? new[] { "isotropic", "anisotropic", "checker" } : new[] { "valid", "invalid", "checker" })
            {
                if (phaseMeasurement)
                    phaseIndices!.UploadDataImmediate(Enumerable.Range(0, frameSize * frameSize).Select(pixel =>
                        workload == "isotropic" ? 1f : workload == "anisotropic" ? 2f : 1f + ((pixel % frameSize + pixel / frameSize) & 1)).ToArray());
                var receiverDepths = new float[frameSize * frameSize];
                for (int pixel = 0; pixel < receiverDepths.Length; pixel++)
                    receiverDepths[pixel] = !phaseMeasurement && (workload == "invalid" || workload == "checker" && (((pixel % frameSize) / 8 + (pixel / frameSize) / 8) & 1) != 0) ? 1 : deviceDepth;
                sceneDepth.UploadDataImmediate(receiverDepths);
                int measuredDraws = phaseMeasurement ? 128 : 16;
                for (int sample = phaseMeasurement ? -5 : -2; sample < (phaseMeasurement ? 10 : 5); sample++)
                {
                    DrawBoundary(2, true); target.Clear(0, 0, 0, 0);
                    using var elapsed = GpuTimerQuery.Create();
                    using var shader = program.UseScope();
                    elapsed.Begin();
                    for (int draw = 0; draw < measuredDraws; draw++) vao.DrawElements(PrimitiveType.Triangles, indices);
                    elapsed.End();
                    double milliseconds = elapsed.GetResultNanoseconds() / 1e6;
                    if (sample >= 0) output.WriteLine($"surface-cost quality={refractionQuality} workload={workload} sample={sample} gpuMs={milliseconds:R} viewport=512x512 draws={measuredDraws}");
                }
                float[] revealage = target[1].ReadPixels();
                int replaced = Enumerable.Range(0, frameSize * frameSize).Count(pixel => revealage[pixel] == 0);
                if (workload == "checker" && !phaseMeasurement) Assert.InRange(replaced, 1, frameSize * frameSize - 1);
                if (phaseMeasurement)
                {
                    float[] values = target[3].ReadPixels();
                    if (workload == "isotropic") isotropicPixels = values;
                    else if (workload == "anisotropic") anisotropicPixels = values;
                    else
                    {
                        int iso = 0, aniso = 0;
                        for (int pixel = 0; pixel < values.Length; pixel += 4)
                        {
                            float first = MathF.Abs(values[pixel] - isotropicPixels![pixel]);
                            float second = MathF.Abs(values[pixel] - anisotropicPixels![pixel]);
                            Assert.InRange(MathF.Min(first, second), 0, 1e-5f * MathF.Max(1, MathF.Abs(values[pixel])));
                            if (first < second) iso++; else if (second < first) aniso++;
                        }
                        Assert.True(iso > 0 && aniso > 0, $"Mixed phase pixels: isotropic={iso}, anisotropic={aniso}");
                    }
                }
                for (int attachment = 0; attachment < 6; attachment++)
                {
                    float[] pixels = target[attachment].ReadPixels();
                    Assert.All(pixels, value => Assert.True(float.IsFinite(value)));
                    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan())));
                    output.WriteLine($"surface-workload-output quality={refractionQuality} workload={workload} mrt={attachment} sum={pixels.Sum(value => (double)value):R} hash={hash} replaced={replaced}");
                    string? captureDirectory = Environment.GetEnvironmentVariable("VGE_WATER_SURFACE_OUTPUTS");
                    if (!string.IsNullOrEmpty(captureDirectory))
                    {
                        Directory.CreateDirectory(captureDirectory);
                        using var file = File.Create(Path.Combine(captureDirectory, $"q{refractionQuality}-{workload}-mrt{attachment}.f32.gz"));
                        using var compressed = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest);
                        compressed.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan()));
                    }
                }
                target.BindWithViewport();
            }
        }

        /// <summary>Submits a sloped quad with an explicitly encoded outward normal and no wave animation.</summary>
        void DrawBoundary(float distance, bool entry)
        {
            float[] points = [-10, -10, 10, -10, 10, 10, -10, -10, 10, 10, -10, 10];
            var data = new float[66];
            var packed = new int[18];
            for (int vertex = 0; vertex < 6; vertex++)
            {
                float x = points[vertex * 2];
                int offset = vertex * 11;
                data[offset] = x;
                data[offset + 1] = points[vertex * 2 + 1];
                data[offset + 2] = -distance + (scenario == 1 ? .05f * x : scenario == 7 ? 2f * x : scenario == 8 ? 1.2f * x : scenario == 9 ? .4f * x : 0);
                // Rotating the world and camera together preserves the same optical geometry.
                Vector3 worldPosition = Vector3.Transform(new(data[offset], data[offset + 1], data[offset + 2]), worldFromView);
                data[offset] = worldPosition.X;
                data[offset + 1] = worldPosition.Y;
                data[offset + 2] = worldPosition.Z;
                if (scenario >= 14)
                {
                    data[offset] = points[vertex * 2] * 10;
                    data[offset + 1] = 0;
                    data[offset + 2] = -points[vertex * 2 + 1] * 10;
                }
                data[offset + 3] = data[offset + 4] = .5f;
                if (phaseMeasurement)
                { data[offset + 3] = x * .05f + .5f; data[offset + 4] = points[vertex * 2 + 1] * .05f + .5f; }
                if (compatibility == FlowCase)
                {
                    data[offset + 3] = data[offset + 4] = .4375f;
                    data[offset + 9] = .25f / 5.5f;
                }
                data[offset + 8] = validationSky;
                packed[vertex * 3] = (7 << 22) | (entry ? 0 : 1 << 21);
                packed[vertex * 3 + 2] = compatibility == LavaCase ? 1 << 27
                    : compatibility is FullAlphaCase or FlowCase ? 1 << 30 : 0;
                // Encode a real atlas footprint so animated wrapping uses a tile base,
                // rather than treating the sample coordinate as a zero-size tile origin.
                if (compatibility == FlowCase) packed[vertex * 3 + 2] |= (112 << 10) | (112 << 18);
                if (scenario >= 14) packed[vertex * 3] = 7 << 18;
            }
            using (vertices.BindScope()) vertices.UploadData(data);
            using (flags.BindScope()) flags.UploadData(packed);
            target.BindWithViewport();
            using var shader = program.UseScope();
            vao.DrawElements(PrimitiveType.Triangles, indices);
        }
    }
    #endregion

    #region Private
    /// <summary>Decodes an authored sRGB channel independently of the production color include.</summary>
    private static float Decode(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    /// <summary>Checks the independently predicted refracted floor sample and premultiplied fallback mixture.</summary>
    private static void AssertFixedWorldSnellGradient(float[] accumulation, float[] fallback, float revealage,
        float fallbackRevealage, int frameSize, Vector3 camera, Matrix4x4 worldFromView, Matrix4x4 viewProjection, bool sceneLinear)
    {
        // Intersect the camera ray with the interface, apply Snell's law, then intersect the fixed floor.
        Vector3 incident = Vector3.Normalize(Vector3.TransformNormal(new(1 / ((float)frameSize * MathF.Sqrt(3)),
            1 / ((float)frameSize * MathF.Sqrt(3)), -1), worldFromView));
        Vector3 surface = camera + incident * (-camera.Y / incident.Y);
        const float eta = 1 / 1.333f;
        float cosine = -incident.Y;
        float transmittedCosine = MathF.Sqrt(1 - eta * eta * (1 - cosine * cosine));
        Vector3 direction = eta * incident + (eta * cosine - transmittedCosine) * Vector3.UnitY;
        Vector3 receiver = surface + direction * (-3 / direction.Y);
        Vector4 clip = Vector4.Transform(new Vector4(receiver, 1), viewProjection);
        int x = (int)((clip.X / clip.W * .5f + .5f) * frameSize);
        int y = (int)((clip.Y / clip.W * .5f + .5f) * frameSize);
        Assert.InRange(x, 0, frameSize - 1);
        Assert.InRange(y, 0, frameSize - 1);
        Vector3 sampleRay = Vector3.TransformNormal(new((2f * (x + .5f) / frameSize - 1) / MathF.Sqrt(3),
            (2f * (y + .5f) / frameSize - 1) / MathF.Sqrt(3), -1), worldFromView);
        Vector3 sampledFloor = camera + sampleRay * (-5 / sampleRay.Y);
        float rs = (eta * cosine - transmittedCosine) / (eta * cosine + transmittedCosine);
        float rp = (cosine - eta * transmittedCosine) / (cosine + eta * transmittedCosine);
        float rawRed = MathF.Max(.1f, 2 + sampledFloor.X * .02f) * (1 - .5f * (rs * rs + rp * rp));
        float alpha = 1 - revealage;
        float fallbackAlpha = 1 - fallbackRevealage;
        // No direct/environment/source light exists here, so the fallback contribution is black.
        Assert.InRange(MathF.Abs(fallback[0]), 0, .00001f);
        float confidence = (alpha - fallbackAlpha) / (1 - fallbackAlpha);
        float expectedRed = rawRed * confidence / alpha;
        if (!sceneLinear)
        {
            float mapped = expectedRed / (1 + expectedRed);
            expectedRed = 1.055f * MathF.Pow(mapped, 1 / 2.4f) - .055f + (.5f / 64 - .5f) / 255;
        }
        Assert.InRange(accumulation[0] / accumulation[3], expectedRed - .003f, expectedRed + .003f);
    }

    /// <summary>Predicts receiver sampling independently for pixel-normal distortion and traced Snell optics.</summary>
    private static void AssertReceiverGradient(float[] accumulation, bool sceneLinear, int scatteringSource, int refractionQuality)
    {
        float raySlope = 1f / (16f * MathF.Sqrt(3));
        float z = -2f / (1f + .4f * raySlope);
        Vector3 surface = new(-z * raySlope, -z * raySlope, z);
        Vector3 incident = Vector3.Normalize(surface);
        Vector3 normal = Vector3.Normalize(new(-.4f, 0, 1));
        float cosine = -Vector3.Dot(normal, incident);
        const float eta = 1f / 1.333f;
        float transmittedCosine = MathF.Sqrt(1f - eta * eta * (1f - cosine * cosine));
        Vector3 direction = eta * incident + (eta * cosine - transmittedCosine) * normal;
        Vector3 receiver = refractionQuality == 0 ? PixelNormalReceiver(surface, normal)
            : surface + direction * ((-10f - z) / direction.Z);
        // Traced qualities sample the texel containing the physical endpoint. Pixel-normal
        // distortion reconstructs a continuous camera-ray sample on this planar gradient.
        int pixel = (int)((receiver.X / 10f * MathF.Sqrt(3f) * .5f + .5f) * 16f);
        if (refractionQuality != 0) Assert.NotEqual(8, pixel);
        float rs = (eta * cosine - transmittedCosine) / (eta * cosine + transmittedCosine);
        float rp = (cosine - eta * transmittedCosine) / (cosine + eta * transmittedCosine);
        float transmission = 1f - .5f * (rs * rs + rp * rp);
        float rawRed = (1f + pixel * .2f) * transmission;
        if (refractionQuality == 0)
        {
            float coordinate = (receiver.X / 10 * MathF.Sqrt(3) * .5f + .5f) * 16 - .5f;
            rawRed = (1 + coordinate * .2f) * transmission;
        }
        if (scatteringSource != 0)
        {
            Vector3 light = scatteringSource == 1 ? new(0, 0, -1) : Vector3.Normalize(new Vector3(0, 0, -12) - surface);
            float intensity = scatteringSource == 1 ? 100 : 10000 / Vector3.DistanceSquared(new(0, 0, -12), surface);
            double phaseCosine = Vector3.Dot(-light, -direction);
            double phase = (1 - .7 * .7) / (4 * Math.PI * Math.Pow(1 + .7 * .7 - 2 * .7 * phaseCosine, 1.5));
            double path = refractionQuality == 0 ? -Vector3.Dot(receiver - surface, normal) / transmittedCosine
                : Vector3.Distance(receiver, surface);
            double[] extinction = [.3, .5, .7], scatter = [.2, .3, .4];
            for (int channel = 0; channel < 3; channel++)
            {
                double expected = transmission * intensity * phase * scatter[channel] / extinction[channel]
                    * (1 - Math.Exp(-extinction[channel] * path));
                Assert.InRange((double)(accumulation[channel] / accumulation[3]), expected * .995, expected * 1.005);
            }
            return;
        }
        if (sceneLinear)
        {
            Assert.InRange(accumulation[0] / accumulation[3], rawRed - .002f, rawRed + .002f);
            return;
        }
        float linearRed = rawRed / (1f + rawRed);
        float encodedRed = 1.055f * MathF.Pow(linearRed, 1f / 2.4f) - .055f;
        float dither = (.5f / 64f - .5f) / 255f;
        Assert.InRange(accumulation[0] / accumulation[3], encodedRed + dither - .002f, encodedRed + dither + .002f);
    }

    /// <summary>Projects authored normal-detail distortion and intersects its camera ray with the constant-depth receiver.</summary>
    private static Vector3 PixelNormalReceiver(Vector3 surface, Vector3 normal)
    {
        // The fixture packs +Z as its mesh normal while its sloped vertices produce a
        // different geometric normal. Their difference is the authored detail signal.
        const float receiverDepth = 10f;
        float focalLength = MathF.Sqrt(3f);
        Vector2 seed = new(surface.X / -surface.Z, surface.Y / -surface.Z);
        seed = seed * (focalLength * .5f) + new Vector2(.5f);
        float shallow = Math.Clamp((surface.Z + receiverDepth) / .3f, 0f, 1f);
        Vector2 uv = seed - new Vector2(normal.X, normal.Y) * (focalLength * .02f * shallow);
        uv = Vector2.Clamp(uv, new(.55f / 16f), new(1f - .55f / 16f));
        Vector2 receiverXY = (uv * 2f - Vector2.One) * (receiverDepth / focalLength);
        return new(receiverXY, -receiverDepth);
    }

    /// <summary>Publishes row-vector Numerics storage as engine column-major matrix bytes.</summary>
    private static float[] Flatten(Matrix4x4 value) => [value.M11,value.M12,value.M13,value.M14,
        value.M21,value.M22,value.M23,value.M24,value.M31,value.M32,value.M33,value.M34,value.M41,value.M42,value.M43,value.M44];
    #endregion
}
