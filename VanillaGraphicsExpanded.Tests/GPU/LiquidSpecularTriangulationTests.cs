using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
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
        using var publication = new TestFrameLightingPublication();
        var program = GpuShaderProgramLibraries.Declare(assets.Api, LiquidShaderProgram.Contract.Identity, () => new LiquidShaderProgram());
        program.FrameLightingInputs = publication.Buffer;
        program.CaptureMode = 1;
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();

        const int size = 256;
        using var target = CreateRenderTarget(size, size, TextureFormat.Rgba8);
        using var terrain = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var material = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var aerial = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        terrain.UploadDataImmediate(new float[] { 1, 1, 1, 1 });
        // Broaden the diagnostic sun glint so the fold can be inspected at this capture resolution.
        material.UploadDataImmediate([.2f, 0, 0, 1]);
        depth.UploadDataImmediate(new float[] { 1 });
        aerial.UploadDataImmediate(new float[8], 0, 0, 0, 1, 2, 1, 0);
        program.TerrainTexture = terrain;
        program.MaterialParamsTexture = material;
        program.DepthTexture = depth;
        using var shadow = new DepthTexture(1, 1, TextureFormat.DepthComponent32f);
        shadow.UploadDataImmediate([1f]);
        program.ShadowMapNear = shadow;
        program.ShadowMapFar = shadow;
        program.AerialRadianceTexture = aerial;
        program.AerialAttenuationTexture = aerial;
        program.ShadowRanges = Vector4.Zero;
        // A low solar elevation stretches the water highlight across the folded faces.
        program.SunDirection = new(Vector3.Normalize(new(0, .4f, -5)), 0);
        program.SolarIrradiance = new(1, 1, 1, 0);
        program.EnvironmentIrradiance = Vector4.Zero;
        program.AtlasMetrics = new(1, 1, 1, 1);

        program.WaveFrame = new(Vector4.Zero, 1);
        program.SetFogSphereCount(0);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1, .1f, 100);
        projection.M33 = -(100 + .1f) / (100 - .1f);
        projection.M43 = -2 * 100 * .1f / (100 - .1f);
        using var frameCamera = TestFrameCamera.CreateFromProjection(Flatten(projection), size, size, .1f, 100);
        using var sharedLights = new VgeLightsUniformBuffer();
        program.LightsInputs = sharedLights;
        program.FrameInputs = frameCamera;
        var view = Matrix4x4.CreateRotationX(18 * MathF.PI / 180);
        program.ModelViewMatrix = Flatten(view);


        // Identical packed +Y normals and animated-water flags isolate the fold in the mesh.
        const int up = 7 << 18;
        using var mesh = new LiquidMeshFixture(new float[] {
                -2,-1,-4, .5f,.5f, 0,0,0,1, 0,0,
                 2,-1,-4, .5f,.5f, 0,0,0,1, 0,0,
                 2,-1,-8, .5f,.5f, 0,0,0,1, 0,0,
                -2,-.45f,-8, .5f,.5f, 0,0,0,1, 0,0 }, new int[] {
                up,0,1 | (255 << 2),
                up,0,1 | (255 << 2),
                up,0,1 | (255 << 2),
                up,0,1 | (255 << 2)
            }, [0, 1, 2, 0, 2, 3]);
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

            program.TerrainTexture = terrain;
            program.MaterialParamsTexture = material;
            program.DepthTexture = depth;
            program.ShadowMapNear = shadow;
            program.ShadowMapFar = shadow;

            mesh.SetTopology(topology);
            Submit(wire: false);
            float[] radiance = target[0].ReadPixels();
            if (capture is not null)
            {
                string path = Path.Combine(Path.GetDirectoryName(capturePath!) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(capturePath) + $"-{label}.bmp");
                capture.Capture(target, path);
                // The wire variant shares the production vertex stage and marks actual GL-rasterized edges.
                program.CaptureMode = 2;
                Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
                Submit(wire: true);
                capture.Capture(target, Path.Combine(Path.GetDirectoryName(capturePath!) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(capturePath) + $"-{label}-Wire.bmp"));
            }
            return radiance;
        }

        /// <summary>Draws the exact prepared generation with explicit packing, target and raster policy.</summary>
        void Submit(bool wire)
        {
            var commands = RHICommandContext.Current;
            var pass = FramebufferTestSetup.DescribeAttachments(target,
                Enumerable.Range(0, target.ColorAttachmentCount).Select(index => new RHIColorAttachment(AttachmentIndex: index,
                    Load: wire ? AttachmentLoad.Preserve : AttachmentLoad.Clear,
                    Clear: ColorClearValue.Float(0, 0, 0, 0))));
            using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, LiquidMeshFixture.Layout,
                commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport,
                rasterizer: new() { PolygonMode = wire ? RasterizerFillMode.Line : RasterizerFillMode.Fill })).RequireResource();
            using var boundary = commands.EnterBoundary(new("Tests.LiquidTriangulation", true,
                graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass); commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(program.CaptureBindings());
                commands.DrawIndexed(mesh.Inputs, new(0, mesh.IndexCount));
                commands.EndRenderPass();
            });
        }
    }
    #endregion

    #region Private
    /// <summary>Converts row-vector Numerics storage into the shader's column-vector representation.</summary>
    private static float[] Flatten(Matrix4x4 value) => MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref value, 1)).ToArray();
    #endregion
}
