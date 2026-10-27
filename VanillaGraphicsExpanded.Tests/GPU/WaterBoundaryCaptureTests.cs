using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Rasterizes actual production liquid boundary SPIR-V into the additive optical-depth targets.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterBoundaryCaptureTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Captures sloped entry/exit faces, separated water bodies, opaque foreground clipping and sky-depth capture bounds.</summary>
    [Theory]
    [InlineData(0, 2f, 0f)]
    [InlineData(1, 5f, 0f)]
    [InlineData(2, 0f, 0f)]
    [InlineData(3, 2f, 0f)]
    [InlineData(4, -7f, -1f)]
    [InlineData(5, -97f, -1f)]
    [InlineData(6, 2f, 0f)]
    [InlineData(7, 2f, 0f)]
    [InlineData(8, 2f, 0f)]
    [InlineData(9, 2f, 0f)]
    [InlineData(7, 2f, 0f, .25f)]
    public void CaptureMeasuresOrientedIntervals(int scenario, float length, float count, float storageScale = 1)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        string? measuredBinary = Environment.GetEnvironmentVariable("VGE_WATER_BOUNDARY_BINARY");
        if (!string.IsNullOrEmpty(measuredBinary))
        {
            byte[] binary = File.ReadAllBytes(measuredBinary);
            assets.BeforeRead = path =>
            {
                if (path.Contains("variants/pbr_liquid.fsh/", StringComparison.Ordinal)) assets.Overrides[path] = binary;
            };
        }
        var program = GpuShaderProgramLibraries.Declare(assets.Api, LiquidShaderProgram.Contract.Identity, () => new LiquidShaderProgram { CaptureMode = 3 }, new VanillaGraphicsExpanded.Rendering.Contracts.ShaderSettings(LiquidShaderProgram.Contract).With("VGE_LIQUID_CAPTURE_MODE", "3"));
        using var publication = new TestFrameLightingPublication();
        publication.Capture(storageScale);
        program.FrameLightingInputs = publication.Buffer;
        using var sharedLights = new VgeLightsUniformBuffer();
        program.LightsInputs = sharedLights;
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        if (!string.IsNullOrEmpty(measuredBinary))
        {
            Assert.NotEmpty(assets.Overrides);
            output.WriteLine($"boundary-device renderer={GL.GetString(StringName.Renderer)} version={GL.GetString(StringName.Version)} binary={measuredBinary}");
        }
        using var transport = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var opticalAttachment = GpuFramebufferAttachment.FromTexture(transport, layer: WaterVolumeFrame.OpticalLayer);
        using var sourceAttachment = GpuFramebufferAttachment.FromTexture(transport, layer: WaterVolumeFrame.SourceLayer);
        using var target = DynamicGpuFramebuffer.Create([opticalAttachment, sourceAttachment]);
        using var terrain = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var material = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var aerial = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        aerial.UploadDataImmediate(new float[4], 0, 0, 0, 1, 1, 1, 0);
        using var mediumIndex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var mediumRecord = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        mediumIndex.UploadDataImmediate([1f]);
        float[] absorption = scenario >= 6 ? [.1f, .2f, .3f] : [.340f, .0565f, .00922f];
        if (scenario == 8) Array.Clear(absorption);
        float scattering = scenario is 7 or 9 ? .25f : 0;
        mediumRecord.UploadDataImmediate([absorption[0], absorption[1], absorption[2], 0, scattering, 0, 0, 0]);
        program.WaterMediumIndicesTexture = mediumIndex;
        program.WaterMediumRecordsTexture = mediumRecord;
        terrain.UploadDataImmediate(new float[] { 1, 1, 1, 1 });
        material.UploadDataImmediate([.1f, 0, 0, 1]);
        const float near = .1f, far = 100;
        float receiver = scenario == 2 ? 1 : 10;
        float deviceDepth = .5f * (1 + (far + near - 2 * far * near / receiver) / (far - near));
        depth.UploadDataImmediate([scenario is 3 or 5 ? 1 : deviceDepth]);
        program.TerrainTexture = terrain;
        program.MaterialParamsTexture = material;
        program.DepthTexture = depth;
        program.AerialRadianceTexture = aerial;
        program.AerialAttenuationTexture = aerial;
        program.ShadowRanges = Vector4.Zero;
        program.SunDirection = new(Vector3.UnitY, 0);
        program.SolarIrradiance = scenario >= 6 ? new(12.56637061436f, 12.56637061436f, 12.56637061436f, 0) : Vector4.Zero;
        program.EnvironmentIrradiance = new(12.56637061436f, 12.56637061436f, 12.56637061436f, 0);

        program.AtlasMetrics = Vector4.One;
        program.SetFogSphereCount(0);
        program.MediumLookupEnabled = scenario >= 6;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1, near, far);
        projection.M33 = -(far + near) / (far - near);
        projection.M43 = -2 * far * near / (far - near);
        using var frameCamera = TestFrameCamera.CreateFromProjection(Flatten(projection), 1, 1, near, far);
        program.FrameInputs = frameCamera;
        program.ModelViewMatrix = Flatten(Matrix4x4.Identity);
        var state = OpenGlStateCache.Current;
        using var fixedFunction = LegacyFixedFunctionReference.Capture(state);
        using var framebuffer = state.BindFramebufferScope(FramebufferTarget.Framebuffer, target.FboId);
        FramebufferTestSetup.BindForEngineRendering(target);

        state.Apply(new GlPipelineDesc(defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
            .With(GlPipelineStateId.CullFaceEnable).With(GlPipelineStateId.ScissorTestEnable).With(GlPipelineStateId.ColorMask),
            nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.BlendEnable).With(GlPipelineStateId.BlendFunc),
            blendFunc: new(BlendingFactorSrc.One, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.One)));
        FramebufferTestSetup.ClearAttachments(target, 0, 0, 0, 0);
        using var vao = NativeResourceSetup.CreateVertexArray("Test.WaterCapture");
        using var vertices = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = "Test.WaterCapture.Vertices" }).RequireResource();
        using var flags = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = "Test.WaterCapture.Flags" }).RequireResource();
        using var binding = OpenGlStateCache.Current.BindVertexArrayScope(vao.VertexArrayId);
        using var indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = "Test.WaterCapture.Indices" }).RequireResource();
        indices.UploadIndices(new uint[] { 0, 1, 2, 3, 4, 5 });
        using (OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(vertices.Role), vertices.BufferId))
        {
            vertices.UploadData(new float[6 * 11]);
            using (OpenGlStateCache.Current.BindVertexArrayScope(vao.VertexArrayId)) OpenGLVertexCalls.ExecuteCoreVertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 44, 0);
            using (OpenGlStateCache.Current.BindVertexArrayScope(vao.VertexArrayId)) OpenGLVertexCalls.ExecuteCoreVertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 44, 12);
            using (OpenGlStateCache.Current.BindVertexArrayScope(vao.VertexArrayId)) OpenGLVertexCalls.ExecuteCoreVertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 44, 20);
            using (OpenGlStateCache.Current.BindVertexArrayScope(vao.VertexArrayId)) OpenGLVertexCalls.ExecuteCoreVertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, 44, 36);
        }
        using (OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(flags.Role), flags.BufferId))
        {
            flags.UploadData(new int[18]);
            using (OpenGlStateCache.Current.BindVertexArrayScope(vao.VertexArrayId)) OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer(3, 1, VertexAttribIntegerType.Int, 12, 0);
            using (OpenGlStateCache.Current.BindVertexArrayScope(vao.VertexArrayId)) OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer(5, 1, VertexAttribIntegerType.Int, 12, 4);
            using (OpenGlStateCache.Current.BindVertexArrayScope(vao.VertexArrayId)) OpenGLVertexCalls.ExecuteCoreVertexAttribIPointer(6, 1, VertexAttribIntegerType.Int, 12, 8);
        }
        bool measuring = false;
        if (scenario is not (4 or 5)) DrawBoundary(2, true);
        DrawBoundary(scenario is 4 or 5 ? 3 : 4, false);
        if (scenario == 1) { DrawBoundary(6, true); DrawBoundary(9, false); }
        var optical = LayeredTestTexture.Read(transport, WaterVolumeFrame.OpticalLayer);
        var source = LayeredTestTexture.Read(transport, WaterVolumeFrame.SourceLayer);
        // The production vertex shader deliberately perturbs clip W by .0008 / Z;
        // its perspective interpolation moves these sloped intersections by under .0004 metres.
        const float rasterTolerance = .0005f;
        for (int channel = 0; channel < 3; channel++)
        {
            float scatteringDepth = channel == 0 ? scattering * (scenario == 9 ? 8 : length) : 0;
            float expectedOptical = absorption[channel] * length + scatteringDepth;
            Assert.InRange(optical[channel], expectedOptical - rasterTolerance, expectedOptical + rasterTolerance);
            // The fixture supplies 4*pi solar lux and 4*pi environment nits,
            // so isotropic source radiance is 1 + pi before scattering weighting.
            float expectedSource = (1 + MathF.PI) * scatteringDepth * storageScale;
            Assert.InRange(source[channel], expectedSource - rasterTolerance, expectedSource + rasterTolerance);
        }
        Assert.InRange(optical[3], length - rasterTolerance, length + rasterTolerance);
        Assert.Equal(count, source[3]);

        if (scenario == 6 && !string.IsNullOrEmpty(measuredBinary))
        {
            // This opt-in draw-only comparison uses the same production owner and
            // fixed workload for externally supplied, equally optimized binaries.
            const int size = 512;
            using var measuredTarget = CreateMRTRenderTarget(size, size, TextureFormat.Rgba32f, TextureFormat.Rgba32f);
            using var measuredDepth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var indicesTexture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var recordsTexture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var shadow = new DepthTexture(1, 1, TextureFormat.DepthComponent32f);
            measuredDepth.UploadDataImmediate(Enumerable.Repeat(deviceDepth, size * size).ToArray());
            bool phaseMeasurement = Environment.GetEnvironmentVariable("VGE_MEASURE_WATER_PHASE") == "1";
            recordsTexture.UploadDataImmediate([.1f, .2f, .3f, 0, 0, 0, 0, 0, .1f, .2f, .3f, 0, .25f, 0, 0, 0]);
            if (phaseMeasurement)
                recordsTexture.UploadDataImmediate([.1f, .2f, .3f, 0, .25f, 0, 0, 0, .1f, .2f, .3f, .7f, .25f, 0, 0, 0]);
            shadow.UploadDataImmediate([1f]);
            program.ShadowMapNear = shadow; program.ShadowMapFar = shadow;
            program.ShadowRanges = new(100, 100, 0, 0);
            float[] shadowMatrix = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, .5f, .5f, .5f, 1];
            program.ShadowMatrixNear = shadowMatrix; program.ShadowMatrixFar = shadowMatrix;
            program.SolarIrradiance = new(10, 10, 10, 0);
            program.DepthTexture = measuredDepth;
            program.WaterMediumIndicesTexture = indicesTexture; program.WaterMediumRecordsTexture = recordsTexture;

            using var measuredCamera = TestFrameCamera.CreateFromProjection(Flatten(projection), size, size, near, far);
            program.FrameInputs = measuredCamera;
            FramebufferTestSetup.BindForEngineRendering(measuredTarget); measuring = true;
            float[]? isotropicPixels = null, anisotropicPixels = null;
            foreach (string workload in phaseMeasurement ? new[] { "isotropic", "anisotropic", "checker" } : new[] { "clear", "scattering", "checker" })
            {
                indicesTexture.UploadDataImmediate(Enumerable.Range(0, size * size).Select(pixel =>
                    workload is "clear" or "isotropic" ? 1f : workload is "scattering" or "anisotropic" ? 2f : 1f + ((pixel % size + pixel / size) & 1)).ToArray());
                for (int sample = -2; sample < 5; sample++)
                {
                    DrawBoundary(2, true);
                    FramebufferTestSetup.ClearAttachments(measuredTarget, 0, 0, 0, 0);
                    using var elapsed = GpuTimerQuery.Create();
                    using var shader = ShaderBindingTestPipeline.Create(program);
        RHICommandContext.Current.SetPipeline(shader);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
                    elapsed.Begin();
                    for (int draw = 0; draw < 16; draw++) GL.DrawElements(PrimitiveType.Triangles, indices.IndexCount, DrawElementsType.UnsignedInt, IntPtr.Zero);
                    elapsed.End();
                    double milliseconds = elapsed.GetResultNanoseconds() / 1e6;
                    if (sample >= 0) output.WriteLine($"boundary-cost binary={Path.GetFileName(measuredBinary)} workload={workload} sample={sample} gpuMs={milliseconds:R} viewport=512x512 draws=16 shadows=active");
                }
                float[] measuredSource = measuredTarget[1].ReadPixels();
                float[] red = Enumerable.Range(0, size * size).Select(pixel => measuredSource[pixel * 4]).ToArray();
                Assert.All(red, value => Assert.True(float.IsFinite(value)));
                if (workload == "checker" && !phaseMeasurement) { Assert.Contains(0f, red); Assert.Contains(red, value => value > 0); }
                if (phaseMeasurement) Assert.All(red, value => Assert.True(value > 0));
                if (phaseMeasurement)
                {
                    if (workload == "isotropic") isotropicPixels = red;
                    else if (workload == "anisotropic") anisotropicPixels = red;
                    else
                    {
                        int iso = 0, aniso = 0;
                        for (int pixel = 0; pixel < red.Length; pixel++)
                        {
                            float first = MathF.Abs(red[pixel] - isotropicPixels![pixel]);
                            float second = MathF.Abs(red[pixel] - anisotropicPixels![pixel]);
                            Assert.InRange(MathF.Min(first, second), 0, 1e-5f * MathF.Max(1, MathF.Abs(red[pixel])));
                            if (first < second) iso++; else if (second < first) aniso++;
                        }
                        Assert.True(iso > 0 && aniso > 0, $"Mixed phase pixels: isotropic={iso}, anisotropic={aniso}");
                    }
                }
                output.WriteLine($"boundary-output workload={workload} redSum={red.Sum(value => (double)value):R} zeroPixels={red.Count(value => value == 0)}");
                string? outputDirectory = Environment.GetEnvironmentVariable("VGE_WATER_BOUNDARY_OUTPUTS");
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                    for (int attachment = 0; attachment < 2; attachment++)
                    {
                        float[] values = measuredTarget[attachment].ReadPixels();
                        File.WriteAllBytes(Path.Combine(outputDirectory, $"{workload}-mrt{attachment}.f32"),
                            System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
                    }
                }
                FramebufferTestSetup.BindForEngineRendering(measuredTarget);
            }
        }


        /// <summary>Submits a sloped quad with an explicitly encoded outward normal and no wave animation.</summary>
        void DrawBoundary(float distance, bool entry)
        {
            if (scenario == 9)
                mediumRecord.UploadDataImmediate([absorption[0], absorption[1], absorption[2], 0, entry ? scattering : 0, 0, 0, 0]);
            float[] points = [-10, -10, 10, -10, 10, 10, -10, -10, 10, 10, -10, 10];
            var data = new float[66];
            var packed = new int[18];
            for (int vertex = 0; vertex < 6; vertex++)
            {
                float x = points[vertex * 2];
                int offset = vertex * 11;
                data[offset] = x;
                data[offset + 1] = points[vertex * 2 + 1];
                data[offset + 2] = -distance + .05f * x;
                data[offset + 3] = measuring ? x * .05f + .5f : .5f;
                data[offset + 4] = measuring ? points[vertex * 2 + 1] * .05f + .5f : .5f;
                data[offset + 8] = 1;
                packed[vertex * 3] = (7 << 22) | (entry ? 0 : 1 << 21);
            }
            using (OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(vertices.Role), vertices.BufferId)) vertices.UploadData(data);
            using (OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(flags.Role), flags.BufferId)) flags.UploadData(packed);
            using var shader = ShaderBindingTestPipeline.Create(program);
        RHICommandContext.Current.SetPipeline(shader);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
            GL.DrawElements(PrimitiveType.Triangles, indices.IndexCount, DrawElementsType.UnsignedInt, IntPtr.Zero);
        }
    }
    #endregion

    #region Private
    /// <summary>Publishes row-vector Numerics storage as engine column-major matrix bytes.</summary>
    private static float[] Flatten(Matrix4x4 value) => [value.M11,value.M12,value.M13,value.M14,
        value.M21,value.M22,value.M23,value.M24,value.M31,value.M32,value.M33,value.M34,value.M41,value.M42,value.M43,value.M44];
    #endregion
}
