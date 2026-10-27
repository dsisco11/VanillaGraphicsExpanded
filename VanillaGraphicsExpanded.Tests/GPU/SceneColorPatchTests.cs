using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks installed scene producer boundaries under both color conventions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorPatchTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Actual imported sky/cloud noise leaves HDR RGB and coverage unchanged while preserving offscreen display noise.</summary>
    [Theory]
    [InlineData("clouds.fsh")]
    public void InstalledDisplayNoiseIsIsolatedFromSceneStorage(string name)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        var tree = SyntaxTree.Parse(PbrSurfaceInstalledShaderTests.Build(name, 1, 1, 0, 0, 0), GlslSchema.Instance);
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).Single();
        bool cloud = name == "clouds.fsh";
        tree.CreateEditor().Replace(main, "void main(){" + (cloud ? "OITaccumulation0" : "outColor")
            + "=NoiseFromPixelPosition(ivec2(gl_FragCoord.xy),256,1920);}").Commit();
        using var program = Link(shaders, tree.ToText());
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(4, 4, Enumerable.Repeat(TextureFormat.Rgba32f, cloud ? 6 : 1).ToArray());
        foreach (int linearScene in new[] { 0, 1 })
        {
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), linearScene);
            framework.RenderQuadTo(program.ProgramId, target);
            float[] actual = target[cloud ? 3 : 0].ReadPixels();
            int[] indexFactors = [41, 29, 53, 43], areaFactors = [23, 59, 47, 37];
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            for (int channel = 0; channel < 4; channel++)
            {
                int index = 1920 * y + x, area = x * y;
                int hash = (256 + index * indexFactors[channel]) ^ (256 + area * areaFactors[channel]);
                float expected = linearScene != 0 ? 0 : ((hash % 661) / 330.5f - 1) / 128;
                Assert.InRange(actual[(y * 4 + x) * 4 + channel], expected - 1e-7f, expected + 1e-7f);
            }
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Every supported installed fragment compiles after imports and production transforms.</summary>
    [Theory]
    [InlineData("particlesquad.fsh")]
    [InlineData("particlesquad2d.fsh")]
    [InlineData("clouds.fsh")]
    [InlineData("cloudvolumetric.fsh")]
    [InlineData("aurora.fsh")]
    [InlineData("blockhighlights.fsh")]
    public void InstalledFragmentCompiles(string name)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string source = PbrSurfaceInstalledShaderTests.Build(name, 1, 1, 0, 0, 0);
        shaders.Compile(ShaderType.FragmentShader, source);
        if (name == "cloudvolumetric.fsh")
        {
            int decode = source.IndexOf("col.rgb = VgeStoreSceneLighting(VgeCompatibilityLuminance(col.rgb))", StringComparison.Ordinal);
            Assert.True(decode > source.IndexOf("vec4 col = texelFetch(cloudCol", StringComparison.Ordinal));
            Assert.True(decode < source.IndexOf("OITreveal[i] *=", StringComparison.Ordinal));
            string main = SyntaxTree.Parse(source, GlslSchema.Instance).Select(Query.Syntax<GlFunctionNode>().Named("main"))
                .OfType<GlFunctionNode>().Single().Body.ToText();
            // Post-traversal display noise modifies alpha as well as RGB. HDR cloud
            // coverage must keep the integrated value while offscreen draws retain it.
            Assert.Matches(@"if\s*\(\s*vge_sceneLinear\s*==\s*0\s*\)\s*k\s*=\s*exp", main);
        }
    }

    /// <summary>The real engine bucket function receives decoded straight RGB before alpha weighting.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LegacyOitDecodesBeforePremultiplication(int linearScene)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        var tree = SyntaxTree.Parse(PbrSurfaceInstalledShaderTests.Build("particlesquad.fsh", 1, 1, 0, 0, 0), GlslSchema.Instance);
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).Single();
        tree.CreateEditor().Replace(main, "void main(){OIT(vec4(.5,.25,.75,.4),.2,30.0);}").Commit();
        using var program = Link(shaders, tree.ToText());
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(TextureFormat.Rgba32f, 6).ToArray());
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), linearScene);
        framework.RenderQuadTo(program.ProgramId, target);
        float[] accumulation = target[3].ReadPixels();
        float[] reveal = target[1].ReadPixels();
        float[] glow = target[2].ReadPixels();
        float[] straight = [.5f, .25f, .75f];
        for (int channel = 0; channel < 3; channel++)
        {
            float expected = linearScene == 0 ? straight[channel] : 32 * Decode(straight[channel]);
            Assert.InRange(accumulation[channel] / accumulation[3], expected - .00001f, expected + .00001f);
        }
        Assert.InRange(reveal[0], .59999f, .60001f);
        Assert.InRange(glow[0], .19999f, .20001f);
        Assert.InRange(glow[3], .39999f, .40001f);
    }

    /// <summary>The installed traversal integrates decoded cloud samples while retaining extinction and alpha.</summary>
    [Theory]
    [InlineData(0, .4f)]
    [InlineData(1, .4f)]
    [InlineData(0, 1f)]
    [InlineData(1, 1f)]
    public void InstalledCloudTraversalDecodesBeforeIntegration(int linearScene, float sampleAlpha)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        var tree = SyntaxTree.Parse(PbrSurfaceInstalledShaderTests.Build("cloudvolumetric.fsh", 1, 1, 0, 0, 0), GlslSchema.Instance);
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).Single();
        // Supply one controlled voxel segment; the installed volume and traversal functions
        // still perform the actual integration and stop at the first cell exit.
        tree.CreateEditor().Replace(main, "void main(){OITreveal=vec4(1);OITaccumulation0=traverse(vec3(.5,0,.5),vec3(.1,1,.1),.25,0.0);}").Commit();
        using var program = Link(shaders, tree.ToText());
        using var framework = new ShaderTestFramework();
        using var map = framework.CreateTexture(1, 1, TextureFormat.Rgba32f, new[] { 1f, 0f, 0f, 1f });
        using var color = framework.CreateTexture(1, 1, TextureFormat.Rgba32f, new[] { .5f, .25f, .75f, sampleAlpha });
        using var target = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(TextureFormat.Rgba32f, 6).ToArray());
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), linearScene);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "cloudMap"), 0);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "cloudCol"), 1);
        using var bindMap = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, map.TextureId);
        using var bindColor = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 1, color.TextureId);
        framework.RenderQuadTo(program.ProgramId, target);
        float[] actual = target[3].ReadPixels();
        float coverage = 1 - MathF.Exp(-.25f);
        float[] authored = [.5f, .25f, .75f];
        for (int channel = 0; channel < 3; channel++)
        {
            float expected = coverage * (linearScene == 0 ? authored[channel] : 32 * Decode(authored[channel]));
            Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
        }
        Assert.InRange(actual[3], sampleAlpha * coverage - .00001f, sampleAlpha * coverage + .00001f);
    }
    #endregion

    #region Private
    /// <summary>Links a fixed fullscreen interface to the complete transformed fragment.</summary>
    private static GpuProgramObject Link(TerrainShaderTestFixture shaders, string fragment)
    {
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            layout(location=0) in vec2 position;
            out vec2 invFrameSize; flat out float godrayIntensity; out vec2 texCoord; out vec3 sunPosScreen; out float iGlobalTime; out float intensity; out float direction;
            void main(){gl_Position=vec4(position,0,1);invFrameSize=vec2(1);godrayIntensity=0;texCoord=vec2(.5);sunPosScreen=vec3(0);iGlobalTime=0;intensity=0;direction=0;}
            """);
        return GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, shaders.Compile(ShaderType.FragmentShader, fragment)));
    }

    /// <summary>Loads the exact production color helpers copied by the build.</summary>
    private static string Helper() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_color.glsl"));
    /// <summary>Decodes authored sRGB without invoking production helper code.</summary>
    private static float Decode(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    /// <summary>Encodes the independent shoulder result for the perceptual metric.</summary>
    private static float Encode(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - .055f;
    #endregion
}
