using System.Numerics;
using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks imported terrain eye vectors, parallax and normal fading under equivalent render origins.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class TerrainEyeRelativeShadingTests : RenderTestBase
{
    /// <summary>Uses the shared graphics context for the linked terrain-style shader.</summary>
    public TerrainEyeRelativeShadingTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Public API
    /// <summary>Rotation and translated render origins preserve eye distance, POM displacement and normal attenuation.</summary>
    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(.6f, .35f, 1.6f, -.2f)]
    [InlineData(-.8f, 20f, -9f, 12f)]
    public void ImportedMaterialHelpersPreserveEquivalentEyeGeometry(float angle, float eyeX, float eyeY, float eyeZ)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<EyeRelativeShadingProgram>();
        using var drawing = new ShaderTestFramework();
        using var target = drawing.CreateTestGBuffer(2, 2, TextureFormat.Rgba32f);
        using var texture = drawing.CreateTexture(64, 64, TextureFormat.Rgba32f,
            Enumerable.Range(0, 64 * 64).SelectMany(_ => new[] { .8f, .5f, .9f, .25f }).ToArray());
        using var indices = drawing.CreateTexture(1, 1, TextureFormat.R32f, [1f]);
        using var records = drawing.CreateTexture(2, 1, TextureFormat.Rgba32f, [0f, 0f, 1f, 1f, .04f, 0f, 0f, 0f]);
        program.NormalDepth = texture;
        program.DisplacementIndices = indices;
        program.DisplacementRecords = records;

        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        var pass = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: 0)]);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();

        foreach (float distance in new[] { 10f, 16f })
        {
            Vector3 point = new(2, 1, -distance);
            float[] baseline = Render(Vector3.Zero, 0, point, 1);
            Vector3 eye = new(eyeX, eyeY, eyeZ);
            float[] vector = Render(eye, angle, point + eye, 0);
            Assert.InRange(vector[0], -2.00002f, -1.99998f);
            Assert.InRange(vector[1], -1.00002f, -.99998f);
            Assert.InRange(vector[2], distance - .00002f, distance + .00002f);
            Assert.InRange(vector[3], point.Length() - .00002f, point.Length() + .00002f);
            float[] shaded = Render(eye, angle, point + eye, 1);
            for (int i = 0; i < 4; i++) Assert.InRange(shaded[i], baseline[i] - .00002f, baseline[i] + .00002f);
            if (distance == 10)
                Assert.True(MathF.Abs(baseline[0] - .5f) > .00001f,
                    $"POM must displace the authored nonflat surface; baseline={string.Join(",", baseline.Select(value => value.ToString("G9")))}.");
            float t = Math.Clamp((point.Length() - 8) / 16, 0, 1), blend = 1 - t * t * (3 - 2 * t);
            Vector3 normal = Vector3.Normalize(Vector3.Lerp(Vector3.UnitZ, new(.6f, 0, .8f), blend));
            Assert.InRange(shaded[2], normal.X * .5f + .5f - .00002f, normal.X * .5f + .5f + .00002f);
        }

        /// <summary>Uploads the shared linked uniform once and samples the actual material helpers.</summary>
        float[] Render(Vector3 eye, float radians, Vector3 position, int mode)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            float[] view = [c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0,
                -c * eye.X + s * eye.Y, -s * eye.X - c * eye.Y, -eye.Z, 1];
            using var frameCamera = TestFrameCamera.Create(Vintagestory.API.MathTools.Mat4f.Create(), view, 2, 2);
            program.FrameInputs = frameCamera;
            program.Capture(position, mode);
            using var boundary = commands.EnterBoundary(new("Tests.TerrainEyeRelative", true,
                graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(program.CaptureBindings());
                commands.Draw(geometry.Inputs, new(0, 3));
                commands.EndRenderPass();
            });
            return target[0].ReadPixelsRegion(0, 0, 1, 1);
        }
    }
    #endregion

    #region Shader fixtures
    /// <summary>Expands relative production includes without altering their implementations.</summary>
    internal static string Expand(string path) => Regex.Replace(TestAssetSnapshot.ReadSourceText(path), "@import\\s+\"([^\"]+)\"",
        match => Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups[1].Value))));
    #endregion
}
