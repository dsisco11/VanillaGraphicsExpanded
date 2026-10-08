using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks installed legacy and postprocess boundaries under both scene color conventions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorPatchTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Every supported installed fragment compiles after imports and production transforms.</summary>
    [Theory]
    [InlineData("particlesquad.fsh")]
    [InlineData("particlesquad2d.fsh")]
    [InlineData("clouds.fsh")]
    [InlineData("cloudvolumetric.fsh")]
    [InlineData("aurora.fsh")]
    [InlineData("blockhighlights.fsh")]
    [InlineData("luma.fsh")]
    [InlineData("godrays.fsh")]
    public void InstalledFragmentCompiles(string name)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string source = PbrSurfaceInstalledShaderTests.Build(name, 1, 1, 0, 0, 0);
        shaders.Compile(ShaderType.FragmentShader, source);
        if (name == "cloudvolumetric.fsh")
        {
            int decode = source.IndexOf("col.rgb = VgeSrgbToLinear(col.rgb)", StringComparison.Ordinal);
            Assert.True(decode > source.IndexOf("vec4 col = texelFetch(cloudCol", StringComparison.Ordinal));
            Assert.True(decode < source.IndexOf("OITreveal[i] *=", StringComparison.Ordinal));
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
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        string bucket = File.ReadAllText(Path.Combine(root, "assets/game/shaderincludes/oit.fsh"));
        var tree = SyntaxTree.Parse("#version 430 core\n#define USEOIT 1\n" + Helper() + bucket + "\nvoid main(){OIT(vec4(.5,.25,.75,.4),.2,30.0);}", GlslSchema.Instance);
        SceneColorLegacyPatches.Apply(tree, "particlesquad.fsh");
        using var program = Link(shaders, tree.ToText());
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(PixelInternalFormat.Rgba32f, 6).ToArray());
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), linearScene);
        framework.RenderQuadTo(program.ProgramId, target);
        float[] accumulation = target[3].ReadPixels();
        float[] reveal = target[1].ReadPixels();
        float[] glow = target[2].ReadPixels();
        float[] straight = [.5f, .25f, .75f];
        for (int channel = 0; channel < 3; channel++)
        {
            float expected = linearScene == 0 ? straight[channel] : Decode(straight[channel]);
            Assert.InRange(accumulation[channel] / accumulation[3], expected - .00001f, expected + .00001f);
        }
        Assert.InRange(reveal[0], .59999f, .60001f);
        Assert.InRange(glow[0], .19999f, .20001f);
        Assert.InRange(glow[3], .39999f, .40001f);
    }

    /// <summary>Luma preserves radiance while god rays generate bounded artistic radiance from display-domain samples.</summary>
    [Theory]
    [InlineData("luma.fsh", 0)]
    [InlineData("luma.fsh", 1)]
    [InlineData("godrays.fsh", 0)]
    [InlineData("godrays.fsh", 1)]
    public void InstalledPostprocessUsesCorrectBrightnessDomain(string name, int linearScene)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string source = PbrSurfaceInstalledShaderTests.Build(name, 1, 0, 0, 0, 0);
        using var program = Link(shaders, source);
        using var framework = new ShaderTestFramework();
        using var input = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { 8f, 4f, 2f, .25f });
        using var glow = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { 1f, 1f, 1f, 1f });
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), linearScene);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, name == "luma.fsh" ? "scene" : "inputTexture"), 0);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "glowParts"), 1);
        using var bindInput = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, input.TextureId);
        using var bindGlow = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 1, glow.TextureId);
        framework.RenderQuadTo(program.ProgramId, target);
        float[] pixels = target[0].ReadPixels();
        float[] radiance = [8, 4, 2];
        if (name == "luma.fsh")
        {
            Assert.Equal(radiance, pixels[..3]);
            float[] metric = linearScene == 0 ? radiance : radiance.Select(value => Encode(value / 9f)).ToArray();
            float expected = metric[0] * .299f + metric[1] * .587f + metric[2] * .114f;
            Assert.InRange(pixels[3], expected - .00001f, expected + .00001f);
        }
        else
        {
            float[] display = radiance.Select(value => Encode(value / 9f)).ToArray();
            float factor = linearScene == 0 ? 0 : 1 - Math.Max(display.Average() - .7f, 0);
            for (int channel = 0; channel < 3; channel++)
            {
                float expected = linearScene == 0 ? 0 : Decode(display[channel] * factor);
                Assert.InRange(pixels[channel], expected - .00001f, expected + .00001f);
            }
            Assert.Equal(1, pixels[3]);
        }
    }
    /// <summary>The installed final combines above-one bloom and rays before its single display operator.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InstalledFinalResolvesCombinedRadianceOnce(int linearScene)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string source = PbrSurfaceInstalledShaderTests.Build("final.fsh", 1, 0, 0, 0, 0)
            .Replace("#version 330 core", "#version 330 core\n#define BLOOM 1\n#define GODRAYS 1\n#define FXAA 0");
        using var program = Link(shaders, source);
        using var framework = new ShaderTestFramework();
        using var scene = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { 8f, 8f, 8f, 1f });
        using var bloom = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { 1f, 1f, 1f, 1f });
        using var rays = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { 4f, 4f, 4f, 1f });
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        foreach (var pair in new[] { ("primaryScene", 0), ("bloomParts", 1), ("glowParts", 1), ("godrayParts", 2), ("vge_sceneLinear", linearScene) })
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, pair.Item1), pair.Item2);
        foreach (var pair in new[] { ("gammaLevel", 1f), ("brightnessLevel", 1f), ("ambientBloomLevel", 2f) })
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, pair.Item1), pair.Item2);
        using var bindScene = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, scene.TextureId);
        using var bindBloom = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 1, bloom.TextureId);
        using var bindRays = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 2, rays.TextureId);
        framework.RenderQuadTo(program.ProgramId, target);
        float[] actual = target[0].ReadPixels();
        // Owned HDR composition adds scene 8, already weighted bloom 1, and rays 4 before display.
        float encoded = linearScene == 0 ? 1 : Encode(13f / 14f);
        float expected = MathF.Floor(Math.Clamp(encoded - .4921875f / 255f, 0, 1) * 255f + .5f) / 255f;
        for (int channel = 0; channel < 3; channel++) Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
        Assert.Equal(1, actual[3]);
    }
    /// <summary>The installed traversal integrates decoded cloud samples while retaining extinction and alpha.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InstalledCloudTraversalDecodesBeforeIntegration(int linearScene)
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
        using var map = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { 1f, 0f, 0f, 1f });
        using var color = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { .5f, .25f, .75f, .4f });
        using var target = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(PixelInternalFormat.Rgba32f, 6).ToArray());
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), linearScene);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "cloudMap"), 0);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "cloudCol"), 1);
        using var bindMap = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, map.TextureId);
        using var bindColor = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 1, color.TextureId);
        framework.RenderQuadTo(program.ProgramId, target);
        float[] actual = target[3].ReadPixels();
        float coverage = 1 - MathF.Exp(-.25f);
        float[] authored = [.5f, .25f, .75f];
        for (int channel = 0; channel < 3; channel++)
        {
            float expected = coverage * (linearScene == 0 ? authored[channel] : Decode(authored[channel]));
            Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
        }
        Assert.InRange(actual[3], .4f * coverage - .00001f, .4f * coverage + .00001f);
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
