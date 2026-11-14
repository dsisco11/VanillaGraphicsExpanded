using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;



namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;
/// <summary>Exercises production helper interception independently of engine-local variable names.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrSurfaceCaptureBoundaryTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public PbrSurfaceCaptureBoundaryTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Public API
    #region Capture behavior
    /// <summary>Scene capture ignores later underwater mutations, applies damage once, and preserves the original GUI path.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void CaptureFollowsLightingInputAndPreservesGui(int route, bool renamed)
    {
        // Arrange: Compile a controlled source fixture that varies route and water-variable naming independently.
        EnsureContextValid();
        string source = """
            #version 450 core
            #extension GL_ARB_explicit_attrib_location: enable
            #extension GL_ARB_explicit_uniform_location: warn
            #define USEOIT 0
            #define NORMALVIEW 0
            #define BLOOM 0
            layout(location=0) out vec4 outColor;
            vec3 normal = vec3(0,0,1);
            int renderFlags = 0;
            float glowLevel = 0.0;
            float fogAmount = 0.0;
            float getMatMetallicFromRenderFlags(int flags) { return 0.0; }
            vec3 VgeLocalEnvironment(vec3 block, vec3 sky) { return block + sky * 0.35; }
            vec4 applyPsychedelicEffect(vec4 color, vec3 position, int sub)
            {
                float df = clamp(0.5, 0.09, 1);
                return color * df;
            }
            float getFogLevel(vec4 position, float fogMin, float fogDensity)
            {
                float depth = 1.0;
                float nearnessToPlayer = clamp((8-depth)/8, 0, 0.9);
                return nearnessToPlayer;
            }
            float retainIntegerLoop()
            {
                float value = 0.0;
                for (int i = 0; i < 3; i++) value += 1.0;
                return value;
            }
            // This mechanical capture fixture isolates source routing from photometric calibration.
            vec3 VgeBlockIlluminance(vec3 block, bool worldAccessor) { return block; }
            vec3 VgeDiffuseIncident(vec3 incident) { return incident; }
            vec3 VgeForwardSurface(vec3 color, vec3 n, vec3 p, float fog, float transmission) { return color; }
            vec4 applyFogAndShadow(vec4 rgbaPixel, float fog) { return vec4(rgbaPixel.rgb * 0.5, rgbaPixel.a); }
            void main()
            {
                float b = 0.5;
                outColor = vec4(0.2,0.4,0.6,0.25);
            #if BLOOM == 0
                outColor.rgb *= 1 + glowLevel;
            #endif
                float murkiness = 1.0;
                if (murkiness > 0.0)
                {
                    outColor = applyFogAndShadow(outColor, 0.0);
                    outColor.rgb += vec3(1.0);
                }
                else outColor = applyFogAndShadow(outColor, fogAmount);
                outColor.rgb *= b;
            }
            """;
        if (renamed) source = source.Replace("float murkiness = 1.0;", "").Replace("murkiness", "changedWaterVariable")
            .Replace("float b = 0.5;", "float changedWaterVariable = 1.0; float b = 0.5;");
        string helper = TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_color.glsl"));
        source = source.Replace("layout(location=0)", helper + "\nlayout(location=0)");
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var editor = tree.CreateEditor();
        PbrSurfaceShaderPatches.Apply(tree, editor, "standard.fsh");
        editor.Commit();
        string patchedSource = tree.ToText();
        // Assert: One queued patch removes only the targeted extension and preserves unrelated syntax and source behavior.
        Assert.DoesNotContain("#extension GL_ARB_explicit_attrib_location", patchedSource, StringComparison.Ordinal);
        const string unrelatedExtension = "#extension GL_ARB_explicit_uniform_location: warn";
        Assert.Contains(unrelatedExtension, patchedSource, StringComparison.Ordinal);
        Assert.Contains("uniform int vge_pbrRoute;", patchedSource, StringComparison.Ordinal);
        Assert.True(patchedSource.IndexOf("uniform int vge_pbrRoute;", StringComparison.Ordinal) >
            patchedSource.IndexOf(unrelatedExtension, StringComparison.Ordinal),
            "The inserted declarations must follow a surviving leading extension directive.");
        Assert.Contains("float df = clamp(0.5, 0.09, 1.0);", patchedSource, StringComparison.Ordinal);
        Assert.Contains("float nearnessToPlayer = clamp((8-depth)/8, 0.0, 0.9);", patchedSource, StringComparison.Ordinal);
        Assert.Contains("for (int i = 0; i < 3; i++)", patchedSource, StringComparison.Ordinal);
        int vertex = 0, fragment = 0, program = 0;
        try
        {
            string vertexSource = """
                #version 450 core
                layout(location=0) in vec2 position;
                out vec3 vge_viewPosition;
                out vec3 vge_blockField;
                out vec3 vge_sunIrradiance;
                out float vge_skyVisibility;
                void main()
                {
                    gl_Position = vec4(position, 0, 1);
                    vge_viewPosition = vec3(0.0);
                    vge_blockField = vec3(0.0);
                    vge_sunIrradiance = vec3(0.0);
                    vge_skyVisibility = 1.0;
                }
                """;
            // Act: Link the patched fragment against its complete controlled native-forward varying interface.
            vertex = Compile(ShaderType.VertexShader, vertexSource);
            fragment = Compile(ShaderType.FragmentShader, patchedSource);
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "vge_pbrRoute"), route);
            using var framework = new ShaderTestFramework();
            using var output = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
            framework.RenderNativeReferenceQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            // Assert: Route output reflects the preserved scene input, with deferred alpha owned by capture.
            for (int channel = 0; channel < 3; channel++)
            {
                float encoded = (channel + 1) * .1f;
                float expected = route == 0 ? (encoded + 1f) * .5f : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
                Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
            }
            // Deferred material capture owns a surviving sample; native/forward paths preserve authored alpha.
            float expectedAlpha = route == 1 ? 1f : .25f;
            Assert.InRange(actual[3], expectedAlpha - .00001f, expectedAlpha + .00001f);
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            if (fragment != 0) GL.DeleteShader(fragment);
            if (vertex != 0) GL.DeleteShader(vertex);
        }
    }

    #endregion

    #endregion

    #region Private
    #region Driver compilation
    /// <summary>Compiles transformed engine-style GLSL and disposes failed shader handles.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        string log = GL.GetShaderInfoLog(shader);
        if (compiled == 0) GL.DeleteShader(shader);
        Assert.True(compiled != 0, log);
        return shader;
    }
    #endregion
    #endregion
}
