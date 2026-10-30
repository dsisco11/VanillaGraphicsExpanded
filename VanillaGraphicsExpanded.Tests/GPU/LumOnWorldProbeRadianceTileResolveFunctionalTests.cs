using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks physical radiance and unscaled distance written by the production point resolve.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnWorldProbeRadianceTileResolveFunctionalTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the collection's real OpenGL context.</summary>
    public LumOnWorldProbeRadianceTileResolveFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Decodes persistent RGB before comparing the independent physical vertex input.</summary>
    [Fact]
    public void RadianceTileResolve_WritesExpectedTexel()
    {
        EnsureShaderTestAvailable();

        const int width = 32;
        const int height = 16;

        // Pick a texel that is definitely inside bounds.
        const int atlasX = 7;
        const int atlasY = 9;

        // RGBA16F output; keep values representable and stable across drivers.
        const float r = 0.75f;
        const float g = 0.125f;
        const float b = 0.0f;
        float a = (float)Math.Log(3.0); // example encoded hit distance

        var program = Programs.Create<LumOnWorldProbeRadianceTileResolveShaderProgram>();
        using var output = TestFramework.CreateTestGBuffer(width, height, TextureFormat.Rgba16f);

        // Vertex: (inAtlasCoord.xy, inRadiance.rgba)
        float[] vertexData =
        [
            atlasX, atlasY,
            r, g, b, a,
        ];

        var commands = RHICommandContext.Current;
        using var vertices = commands.CreateVertexBuffer(new() { Role = BufferRole.Vertex,
            Mode = ResourceStorageMode.Mutable, Capacity = vertexData.Length * sizeof(float), Usage = BufferUsage.StaticDraw }).RequireResource();
        vertices.UploadData(vertexData);
        var layout = new RHIVertexLayout([
            new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 24),
            new(1, 4, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 8, 24)]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Points,
            new Dictionary<int, GpuVbo> { [0] = vertices });
        var pass = FramebufferTestSetup.DescribeAttachments(output, [new(AttachmentIndex: 0,
            Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        program.AtlasSize = new(width, height);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport,
            assembly: new() { Topology = PrimitiveTopology.Points })).RequireResource();
        using var boundary = commands.EnterBoundary(new("Tests.ProbeTileResolve", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 1));
            commands.EndRenderPass();
        });

        // Validate output texel.
        var pixels = output[0].ReadPixels();
        int idx = (atlasY * width + atlasX) * 4;

        // Persistent storage encodes RGB only; distance alpha stays in its authored domain.
        Assert.Equal(r, pixels[idx + 0] / LightingStorage.PersistentScale, 2);
        Assert.Equal(g, pixels[idx + 1] / LightingStorage.PersistentScale, 2);
        Assert.Equal(b, pixels[idx + 2] / LightingStorage.PersistentScale, 2);
        Assert.Equal(a, pixels[idx + 3], 2);
    }
}
