using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Captures liquid specular output where two water triangles meet at a fold.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidSpecularTriangulationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Renders both folds of a nonplanar water quad for visual comparison.</summary>
    [Fact]
    public void FoldedWaterQuadProducesComparableCaptures()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        program.CaptureMode = 1;
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        Assert.True(program.TryUse());
        const int size = 256;
        using var target = CreateRenderTarget(size, size, PixelInternalFormat.Rgba8);
        using var terrain = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var material = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f);
        using var aerial = Texture3D.Create(1, 2, 1, PixelInternalFormat.Rgba32f);
        terrain.UploadDataImmediate(new float[] { 1, 1, 1, 1 });
        // Broaden the diagnostic sun glint so the fold can be inspected at this capture resolution.
        material.UploadDataImmediate([.2f, 0, 0, 1]);
        depth.UploadDataImmediate(new float[] { 1 });
        aerial.UploadDataImmediate(new float[8], 0, 0, 0, 1, 2, 1, 0);
        program.TerrainTexture = terrain.TextureId;
        program.MaterialParamsTexture = material.TextureId;
        program.DepthTexture = depth.TextureId;
        program.AerialRadianceTexture = aerial.TextureId;
        program.AerialAttenuationTexture = aerial.TextureId;
        program.ShadowRanges = Vector4.Zero;
        // A low solar elevation stretches the water highlight across the folded faces.
        program.SunDirection = new(Vector3.Normalize(new(0, .4f, -5)), 0);
        program.SolarIrradiance = new(1, 1, 1, 0);
        program.EnvironmentIrradiance = Vector4.Zero;
        program.AtlasMetrics = new(1, 1, 1, 1);
        program.DepthRangeAndFrameSize = new(.1f, 100, size, size);
        program.WaveFrame = new(Vector4.Zero, 1);
        program.SetCounts(0, 0);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1, .1f, 100);
        projection.M33 = -(100 + .1f) / (100 - .1f);
        projection.M43 = -2 * 100 * .1f / (100 - .1f);
        program.ProjectionMatrix = Flatten(projection);
        var view = Matrix4x4.CreateRotationX(18 * MathF.PI / 180);
        program.ModelViewMatrix = Flatten(view);
        program.ApplyInputs();

        var state = GlStateCache.Current;
        using var fixedFunction = state.CaptureLegacyFixedFunctionState();
        using var framebufferBinding = state.BindFramebufferScope(FramebufferTarget.Framebuffer, target.FboId);
        using var vao = GpuVao.Create("Test.LiquidTriangulation.Vao");
        using var vertices = GpuVbo.Create(debugName: "Test.LiquidTriangulation.Vertices");
        using var flags = GpuVbo.Create(debugName: "Test.LiquidTriangulation.Flags");
        using var indices = GpuEbo.Create(debugName: "Test.LiquidTriangulation.Indices");
        using var vertexBinding = vao.BindScope();
        using (vertices.BindScope())
        {
            vertices.UploadData(new float[] {
                -2,-1,-4, .5f,.5f, 0,0,0,1, 0,0,
                 2,-1,-4, .5f,.5f, 0,0,0,1, 0,0,
                 2,-1,-8, .5f,.5f, 0,0,0,1, 0,0,
                -2,-.45f,-8, .5f,.5f, 0,0,0,1, 0,0 });
            vao.AttribPointer(0, 3, VertexAttribPointerType.Float, false, 44, 0);
            vao.AttribPointer(1, 2, VertexAttribPointerType.Float, false, 44, 12);
            vao.AttribPointer(2, 4, VertexAttribPointerType.Float, false, 44, 20);
            vao.AttribPointer(4, 2, VertexAttribPointerType.Float, false, 44, 36);
        }
        using (flags.BindScope())
        {
            // Identical packed +Y normals and animated-water flags isolate the fold in the mesh.
            const int up = 7 << 18;
            flags.UploadData(new int[] {
                up,0,1 | (255 << 2),
                up,0,1 | (255 << 2),
                up,0,1 | (255 << 2),
                up,0,1 | (255 << 2)
            });
            vao.AttribIPointer(3, 1, VertexAttribIntegerType.Int, 12, 0);
            vao.AttribIPointer(5, 1, VertexAttribIntegerType.Int, 12, 4);
            vao.AttribIPointer(6, 1, VertexAttribIntegerType.Int, 12, 8);
        }
        var liquidPipeline = new GlPipelineDesc(
            defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
                .With(GlPipelineStateId.CullFaceEnable).With(GlPipelineStateId.BlendEnable)
                .With(GlPipelineStateId.ScissorTestEnable).With(GlPipelineStateId.ColorMask),
            nonDefaultMask: default,
            name: "Test.LiquidTriangulation");
        state.Apply(liquidPipeline);
        Assert.True(target.CheckStatus(out var error), error);

        string? capturePath = Environment.GetEnvironmentVariable("VGE_CAPTURE_LIQUID_TRIANGULATION");
        using var capture = string.IsNullOrWhiteSpace(capturePath) ? null : new FramebufferBmpCapture(size, size);
        float[] diagonalA = Draw([0, 1, 2, 0, 2, 3], "A");
        float[] diagonalB = Draw([0, 1, 3, 1, 2, 3], "B");
        float largestDifference = 0;
        int compared = 0;
        for (int y = size / 4; y < 3 * size / 4; y++)
        for (int x = size / 4; x < 3 * size / 4; x++)
        {
            int pixel = (y * size + x) * 4;
            if (diagonalA[pixel + 3] <= 0 || diagonalB[pixel + 3] <= 0) continue;
            float a = diagonalA[pixel] / diagonalA[pixel + 3];
            float b = diagonalB[pixel] / diagonalB[pixel + 3];
            largestDifference = MathF.Max(largestDifference, MathF.Abs(a - b));
            compared++;
        }
        Assert.True(compared > 100, $"Expected a shared water interior, compared {compared} pixels.");
        TestContext.Current.TestOutputHelper!.WriteLine($"Largest interior highlight difference: {largestDifference}");
        // The two diagonals form different surfaces when the quad is nonplanar.
        // Their difference is diagnostic only; it cannot establish a shading defect.
        Assert.True(largestDifference > .01f, "The folded geometry did not produce a measurable highlight change.");
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        /// <summary>Draws one index topology into the direct-sun-specular display target.</summary>
        float[] Draw(uint[] topology, string label)
        {
            program.CaptureMode = 1;
            Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
            Assert.True(program.TryUse(), "Could not use the precompiled specular capture variant.");
            program.TerrainTexture = terrain.TextureId;
            program.MaterialParamsTexture = material.TextureId;
            program.DepthTexture = depth.TextureId;
            program.ApplyInputs();
            state.Apply(liquidPipeline);
            indices.UploadIndices(topology);
            target.BindWithViewport();
            target.Clear(0, 0, 0, 0);
            vao.DrawElements(PrimitiveType.Triangles, indices);
            float[] radiance = target[0].ReadPixels();
            if (capture is not null)
            {
                string path = Path.Combine(Path.GetDirectoryName(capturePath!) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(capturePath) + $"-{label}.bmp");
                capture.Capture(target, path);
                // The wire variant shares the production vertex stage and marks actual GL-rasterized edges.
                program.CaptureMode = 2;
                Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
                Assert.True(program.TryUse(), "Could not use the precompiled wire capture variant.");
                program.ApplyInputs();
                state.Apply(liquidPipeline);
                target.BindWithViewport();
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                try { vao.DrawElements(PrimitiveType.Triangles, indices); }
                finally { GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill); }
                capture.Capture(target, Path.Combine(Path.GetDirectoryName(capturePath!) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(capturePath) + $"-{label}-Wire.bmp"));
            }
            return radiance;
        }
    }
    #endregion

    #region Private
    /// <summary>Converts row-vector Numerics storage into the shader's column-vector representation.</summary>
    private static float[] Flatten(Matrix4x4 value) => MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref value, 1)).ToArray();
    #endregion
}
