using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compiles transformed engine GLSL to verify capture scope and material output numerically.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrTerrainCaptureGpuTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared context without loading a game process.</summary>
    public PbrTerrainCaptureGpuTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Engine material capture
    /// <summary>Production patching restores decoded material RGB after forward effects after resolving opaque coverage.</summary>
    [Theory]
    [InlineData("chunkopaque.fsh")]
    [InlineData("chunktopsoil.fsh")]
    public void TransformedEngineMainCompilesAndCapturesMaterial(string name)
    {
        EnsureContextValid();
        string helper = TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes", "pbr_color.glsl"));
        string source = "#version 330 core\n" + helper + """

            layout(location=0) out vec4 outColor;
            void main()
            {
                vec4 texColor = vec4(0.2, 0.4, 0.8, 0.5);
                outColor = texColor;
                float murkiness = 0.0;
                outColor = vec4(9.0, 8.0, 7.0, 0.25);
            #if NORMALVIEW > 0
                outColor.rgb = vec3(0.0);
            #endif
            }
            """;
        var tree = SyntaxTree.Parse(source.ReplaceLineEndings("\r\n"), GlslSchema.Instance);
        PbrTerrainColorPatches.ApplyFragment(tree, name);
        // The base game's shaders are runtime GLSL, so this deliberately exercises that compilation
        // boundary rather than registering an unrelated precompiled replacement fixture.
        int fragment = Compile(ShaderType.FragmentShader, tree.ToText());
        int vertex = Compile(ShaderType.VertexShader, """
            #version 330 core
            layout(location=0) in vec2 position;
            void main() { gl_Position = vec4(position,0,1); }
            """);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var framework = new ShaderTestFramework();
            using var output = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
            framework.RenderNativeReferenceQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            float[] expected = [Linear(.2f), Linear(.4f), Linear(.8f), 1f];
            for (int channel = 0; channel < 4; channel++) Assert.InRange(actual[channel], expected[channel] - .00001f, expected[channel] + .00001f);
        }
        finally { GL.DeleteProgram(program); GL.DeleteShader(vertex); GL.DeleteShader(fragment); }
    }
    #endregion

    #region Two-sided foliage normals
    /// <summary>Real derivatives correct fake UP normals while preserving opaque and authored smooth normals.</summary>
    [Theory]
    [InlineData(false, false, false, 0f, 0f, 1f, 0f)]
    [InlineData(true, false, false, 0f, 0f, 0f, 1f)]
    [InlineData(true, false, true, 0f, 0f, 0f, -1f)]
    [InlineData(true, true, false, 0f, 0f, 0.7071068f, 0.7071068f)]
    [InlineData(true, true, true, 0f, 0f, -0.7071068f, -0.7071068f)]
    [InlineData(false, true, false, .35f, 0f, 0f, 1f)]
    [InlineData(true, true, true, .35f, 0f, 0f, -1f)]
    [InlineData(false, false, true, .01f, 0f, 0f, -1f)]
    public void SurfaceNormalUsesGeometryForTransmissionOrLegacyUp(bool twoSided, bool smooth, bool back, float transmission, float x, float y, float z)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<TerrainNormalInputProgram>(shader => shader.Capture(transmission, twoSided, smooth, back));
        using var framework = new ShaderTestFramework();
        using var output = framework.CreateTestGBuffer(4, 4, TextureFormat.Rgba32f);
        RenderDeclaredInputs(program, output);
        float[] actual = VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuLocalRead.Tuple(output[0],0,0);
        float[] expected = [x, y, z, 1];
        for (int channel = 0; channel < 4; channel++)
            Assert.InRange(actual[channel], expected[channel] - .00001f, expected[channel] + .00001f);
    }
    #endregion

    #region Thin foliage transmission
    /// <summary>Transmission respects shadow visibility and surface direction, with a broad viewing lobe.</summary>
    [Theory]
    [InlineData(1f, false, false, 1f)]
    [InlineData(0f, false, false, 0f)]
    [InlineData(.5f, false, false, .5f)]
    [InlineData(1f, true, false, 0f)]
    [InlineData(1f, false, true, .25f)]
    public void TransmissionRespectsVisibilityAndAngles(float visibility, bool frontLit, bool sideView, float factor)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<FoliageTransmissionInputProgram>(shader =>
        {
            shader.EnergyLookup = MicrofacetEnergyLookup.Prepare();
            shader.EnergySampler = GpuSamplers.LinearClamp;
            shader.Capture(visibility, frontLit, sideView);
        });
        using var framework = new ShaderTestFramework();
        using var output = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        RenderDeclaredInputs(program, output);
        float[] actual = VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuLocalRead.Tuple(output[0],0,0);
        float[] tint = [.2f, .8f, .1f];
        for (int channel = 0; channel < 3; channel++)
        {
            var energy = PBR.Materials.Energy.MicrofacetEnergyTable.Shared.Sample(.8f,sideView ? 0 : 1);
            float remaining=1-PBR.Materials.Energy.MicrofacetEnergyTable.Reflectance(energy,new(.04f)).X;
            float expected = tint[channel] * .5f * factor * remaining / MathF.PI;
            Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
        }
    }
    #endregion

    #region Liquid interface optics
    /// <summary>Checks water reflectance, underwater total internal reflection and bounded missing-depth fallback.</summary>
    [Theory]
    [InlineData(1f, false, .0203732f, 16f)]
    [InlineData(1f, true, .0203732f, 0f)]
    [InlineData(.5f, true, 1f, 0f)]
    [InlineData(0f, false, 1f, 16f)]
    public void LiquidOpticsRemainBounded(float cosine, bool underwater, float reflection, float missingThickness)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<LiquidInterfaceInputProgram>(shader => shader.Capture(cosine, underwater));
        using var framework = new ShaderTestFramework();
        using var output = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        RenderDeclaredInputs(program, output);
        float[] actual = VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuLocalRead.Tuple(output[0],0,0);
        Assert.InRange(actual[0], reflection - .00001f, reflection + .00001f);
        Assert.Equal(missingThickness, actual[1]);
        Assert.Equal(0f, actual[2]);
        if (underwater) Assert.Equal(0f, actual[3]);
        else Assert.InRange(actual[3], .19f, .21f);
    }
    #endregion

    #endregion

    #region Private
    #region Runtime GLSL validation
    /// <summary>Submits the declared fixture over the original oversized triangle through an explicit RHI pass.</summary>
    private static void RenderDeclaredInputs(GpuProgram program, GpuFramebuffer output)
    {
        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([
            new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 8)]);
        using var positions = commands.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 24, Usage = BufferUsage.StaticDraw,
            Label = "Tests.PbrTerrainCapture.Positions"
        }).RequireResource();
        positions.UploadData(new float[] { -1, -1, 3, -1, -1, 3 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo> { [0] = positions });
        var pass = FramebufferTestSetup.DescribeAttachments(output,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var boundary = commands.EnterBoundary(new("Tests.PbrTerrainCapture.Numerical", true,
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
    }

    /// <summary>Compiles actual transformed text and reports driver diagnostics on lexical or interface errors.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        if (compiled != 0) return shader;
        string diagnostics = GL.GetShaderInfoLog(shader);
        GL.DeleteShader(shader);
        Assert.Fail(diagnostics + "\n" + source);
        return 0;
    }

    /// <summary>Evaluates the material's specified transfer function independently.</summary>
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    #endregion
    #endregion
}
