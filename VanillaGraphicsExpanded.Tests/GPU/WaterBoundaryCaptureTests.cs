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
public sealed class WaterBoundaryCaptureTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
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
    public void CaptureMeasuresOrientedIntervals(int scenario, float length, float count)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram { CaptureMode = 3 });
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var terrain = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var material = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f);
        using var aerial = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        terrain.UploadDataImmediate(new float[] { 1, 1, 1, 1 });
        material.UploadDataImmediate([.1f, 0, 0, 1]);
        const float near = .1f, far = 100;
        float receiver = scenario == 2 ? 1 : 10;
        float deviceDepth = .5f * (1 + (far + near - 2 * far * near / receiver) / (far - near));
        depth.UploadDataImmediate([scenario is 3 or 5 ? 1 : deviceDepth]);
        program.TerrainTexture = terrain.TextureId;
        program.MaterialParamsTexture = material;
        program.DepthTexture = depth.TextureId;
        program.AerialRadianceTexture = aerial;
        program.AerialAttenuationTexture = aerial;
        program.ShadowRanges = Vector4.Zero;
        program.SunDirection = new(Vector3.UnitY, 0);
        program.SolarIrradiance = Vector4.Zero;
        program.EnvironmentIrradiance = Vector4.Zero;
        program.DepthRangeAndFrameSize = new(near, far, 1, 1);
        program.AtlasMetrics = Vector4.One;
        program.SetCounts(0, 0);
        program.MediumLookupEnabled = false;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1, near, far);
        projection.M33 = -(far + near) / (far - near);
        projection.M43 = -2 * far * near / (far - near);
        program.ProjectionMatrix = Flatten(projection);
        program.ModelViewMatrix = Flatten(Matrix4x4.Identity);
        var state = GlStateCache.Current;
        using var fixedFunction = state.CaptureLegacyFixedFunctionState();
        using var framebuffer = state.BindFramebufferScope(FramebufferTarget.Framebuffer, target.FboId);
        target.BindWithViewport();

        state.Apply(new GlPipelineDesc(defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
            .With(GlPipelineStateId.CullFaceEnable).With(GlPipelineStateId.ScissorTestEnable).With(GlPipelineStateId.ColorMask),
            nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.BlendEnable).With(GlPipelineStateId.BlendFunc),
            blendFunc: new(BlendingFactorSrc.One, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.One)));
        target.Clear(0, 0, 0, 0);
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
        if (scenario is not (4 or 5)) DrawBoundary(2, true);
        DrawBoundary(scenario is 4 or 5 ? 3 : 4, false);
        if (scenario == 1) { DrawBoundary(6, true); DrawBoundary(9, false); }
        var optical = target[0].ReadPixels();
        var source = target[1].ReadPixels();
        float[] absorption = [.340f, .0565f, .00922f];
        // The production vertex shader deliberately perturbs clip W by .0008 / Z;
        // its perspective interpolation moves these sloped intersections by under .0004 metres.
        const float rasterTolerance = .0005f;
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(optical[channel], absorption[channel] * length - rasterTolerance, absorption[channel] * length + rasterTolerance);
        Assert.InRange(optical[3], length - rasterTolerance, length + rasterTolerance);
        Assert.Equal(count, source[3]);


        /// <summary>Submits a sloped quad with an explicitly encoded outward normal and no wave animation.</summary>
        void DrawBoundary(float distance, bool entry)
        {
            float[] points = [-10,-10, 10,-10, 10,10, -10,-10, 10,10, -10,10];
            var data = new float[66];
            var packed = new int[18];
            for (int vertex = 0; vertex < 6; vertex++)
            {
                float x = points[vertex * 2];
                int offset = vertex * 11;
                data[offset] = x;
                data[offset + 1] = points[vertex * 2 + 1];
                data[offset + 2] = -distance + .05f * x;
                data[offset + 3] = data[offset + 4] = .5f;
                data[offset + 8] = 1;
                packed[vertex * 3] = (7 << 22) | (entry ? 0 : 1 << 21);
            }
            using (vertices.BindScope()) vertices.UploadData(data);
            using (flags.BindScope()) flags.UploadData(packed);
            using var shader = program.UseScope();
            vao.DrawElements(PrimitiveType.Triangles, indices);
        }
    }
    #endregion

    #region Private
    /// <summary>Publishes row-vector Numerics storage as engine column-major matrix bytes.</summary>
    private static float[] Flatten(Matrix4x4 value) => [value.M11,value.M12,value.M13,value.M14,
        value.M21,value.M22,value.M23,value.M24,value.M31,value.M32,value.M33,value.M34,value.M41,value.M42,value.M43,value.M44];
    #endregion
}
