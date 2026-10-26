using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies the installed bucket and resolve arithmetic separately from per-fragment storage containment.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class OitStorageArithmeticTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The owned writer discards invalid color, alpha and glow without modifying any destination attachment.</summary>
    [Fact]
    public void OwnedWriterRejectsExceptionalInputs()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string includes = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        string owned = File.ReadAllText(Path.Combine(includes, "oit.glsl"))
            .Replace("@import \"./pbr_oit_safety.glsl\"", File.ReadAllText(Path.Combine(includes, "pbr_oit_safety.glsl")));
        using var program = Link(shaders, "#version 330 core\n" + owned + "\nuniform vec4 testColor;uniform float testGlow;void main(){writeOit(testColor,testGlow);}");
        using var framework = new ShaderTestFramework();
        using var output = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(PixelInternalFormat.Rgba32f, 6).ToArray());
        int color = GL.GetUniformLocation(program.ProgramId, "testColor"), glow = GL.GetUniformLocation(program.ProgramId, "testGlow");
        foreach (float invalidAlpha in new[] { -.1f, 1.1f, float.NaN, float.PositiveInfinity })
        {
            GL.ProgramUniform4(program.ProgramId, color, 2f, 1f, .5f, invalidAlpha);
            GL.ProgramUniform1(program.ProgramId, glow, 0f);
            framework.RenderQuadTo(program.ProgramId, output, (.125f,.25f,.5f,.75f));
            for (int i=0; i<6; i++) Assert.Equal(new[] {.125f,.25f,.5f,.75f}, output[i].ReadPixels());
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity })
        {
            GL.ProgramUniform4(program.ProgramId, color, invalid, 1f, .5f, .75f);
            framework.RenderQuadTo(program.ProgramId, output, (.125f,.25f,.5f,.75f));
            for (int i=0; i<6; i++) Assert.Equal(new[] {.125f,.25f,.5f,.75f}, output[i].ReadPixels());
            GL.ProgramUniform4(program.ProgramId, color, 2f, 1f, .5f, .75f);
            GL.ProgramUniform1(program.ProgramId, glow, invalid);
            framework.RenderQuadTo(program.ProgramId, output, (.125f,.25f,.5f,.75f));
            for (int i=0; i<6; i++) Assert.Equal(new[] {.125f,.25f,.5f,.75f}, output[i].ReadPixels());
            GL.ProgramUniform1(program.ProgramId, glow, 0f);
        }
        GL.ProgramUniform4(program.ProgramId, color, 2f, 1f, .5f, .75f);
        framework.RenderQuadTo(program.ProgramId, output);
        Assert.Equal(new[] {1.5f,.75f,.375f,.75f}, output[3].ReadPixels());
    }
    /// <summary>The installed wrapper accepts valid negative depth and checks the rounded logarithm argument before bucket arithmetic.</summary>
    [Fact]
    public void PatchedDepthGuardUsesActualRoundedLogInput()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        var tree = SyntaxTree.Parse(PbrSurfaceInstalledShaderTests.Build("particlesquad.fsh", 1, 1, 0, 0, 0), GlslSchema.Instance);
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).Single();
        tree.CreateEditor().Replace(main, """
            void main(){
                int x=int(gl_FragCoord.x);
                float depth=x==0?-31.0:x==1?-30.0:x==2?uintBitsToFloat(floatBitsToUint(-30.0)-1u):x==3?-10.0:0.0;
                OIT(vec4(1,.5,.25,.75),0.0,depth);
                outGlow=vec4((depth/30.0)+1.0,0,0,1);
            }
            """).Commit();
        using var program = Link(shaders, tree.ToText());
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), 1);
        using var framework = new ShaderTestFramework();
        using var output = CreateMRTRenderTarget(5, 1, Enumerable.Repeat(PixelInternalFormat.Rgba32f, 6).ToArray());
        framework.RenderQuadTo(program.ProgramId, output, (.125f,.125f,.125f,.125f));
        float[] reveal = output[1].ReadPixels(), logarithm = output[2].ReadPixels();
        for (int x=0; x<5; x++)
        {
            Assert.Equal(x<2?.125f:.25f, reveal[x*4]);
            if (x>=2) Assert.True(logarithm[x*4]>0);
            for (int bin=0; bin<3; bin++) Assert.All(output[3+bin].ReadPixels(), value=>Assert.True(float.IsFinite(value)));
        }
    }
    /// <summary>Actual additive blending keeps large native accumulators finite while invalid submissions preserve all attachments.</summary>
    [Fact]
    public void PatchedNativeBucketsBlendAndRejectInvalidSubmissions()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        var tree = SyntaxTree.Parse(PbrSurfaceInstalledShaderTests.Build("particlesquad.fsh", 1, 1, 0, 0, 0), GlslSchema.Instance);
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).Single();
        tree.CreateEditor().InsertBefore(main, "uniform vec4 testColor; uniform float testDepth;")
            .Replace(main, "void main(){OIT(testColor,0.0,testDepth);}").Commit();
        using var program = Link(shaders, tree.ToText());
        using var framework = new ShaderTestFramework();
        using var output = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(PixelInternalFormat.Rgba32f, 6).ToArray());
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), 1);
        int color = GL.GetUniformLocation(program.ProgramId, "testColor"), depth = GL.GetUniformLocation(program.ProgramId, "testDepth");
        GL.ProgramUniform4(program.ProgramId, color, 8f, 4f, 2f, .75f);
        GL.ProgramUniform1(program.ProgramId, depth, 0f);
        framework.RenderQuadTo(program.ProgramId, output); // Initializes geometry and fullscreen state.
        output.BindWithViewport();
        output.Clear(0, 0, 0, 0);
        GL.ClearBuffer(ClearBuffer.Color, 0, new[] { 1f, 1f, 1f, 1f });
        GL.ClearBuffer(ClearBuffer.Color, 1, new[] { 1f, 1f, 1f, 1f });
        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        for (int attachment = 0; attachment < 6; attachment++)
            GL.BlendFunc(attachment, attachment < 2 ? BlendingFactorSrc.DstColor : BlendingFactorSrc.One,
                attachment < 2 ? BlendingFactorDest.Zero : BlendingFactorDest.One);
        GL.UseProgram(program.ProgramId);
        try
        {
            for (int submission = 0; submission < 64; submission++) framework.RenderGeometry();
            float[][] before = Enumerable.Range(0, 6).Select(i => output[i].ReadPixels()).ToArray();
            Assert.True(before[3][0] > 65504);
            float[] authored = [8, 4, 2];
            for (int channel = 0; channel < 3; channel++)
            {
                double expected = 32 * Math.Pow((authored[channel] + .055) / 1.055, 2.4);
                Assert.InRange(Math.Abs(before[3][channel] / before[3][3] - expected), 0, expected * 4e-6);
            }
            // None of these unsupported fragments may damage previously blended history.
            foreach (float invalidDepth in new[] { -30f, -31f, float.NaN, float.PositiveInfinity })
            { GL.ProgramUniform1(program.ProgramId, depth, invalidDepth); framework.RenderGeometry(); }
            GL.ProgramUniform1(program.ProgramId, depth, 0f);
            foreach (float invalidAlpha in new[] { -.1f, 1.1f, float.NaN, float.PositiveInfinity })
            { GL.ProgramUniform4(program.ProgramId, color, 8f, 4f, 2f, invalidAlpha); framework.RenderGeometry(); }
            foreach (float invalidRgb in new[] { float.NaN, float.PositiveInfinity })
            { GL.ProgramUniform4(program.ProgramId, color, invalidRgb, 4f, 2f, .75f); framework.RenderGeometry(); }
            for (int attachment = 0; attachment < 6; attachment++) Assert.Equal(before[attachment], output[attachment].ReadPixels());
        }
        finally
        {
            GL.UseProgram(0); GL.Disable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
            GpuFramebuffer.Unbind(); StateCache.Current.InvalidateAll();
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>The actual patched resolver preserves the native cutoff and discards exceptional records before half storage.</summary>
    [Fact]
    public void PatchedResolveRetainsCutoffAndProtectsHalfDestination()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        using var program = Link(shaders, PbrSurfaceInstalledShaderTests.Build("transparentcompose.fsh", 1, 1, 0, 0, 0));
        using var framework = new ShaderTestFramework();
        const int width = 9;
        float[] records = new float[width * 4];
        for (int column = 0; column < width; column++)
        {
            records[column * 4] = records[column * 4 + 1] = records[column * 4 + 2] = column == 3 ? 1e30f : 8;
            records[column * 4 + 3] = column == 0 ? MathF.BitDecrement(.0001f) : .0001f;
        }
        records[4 * 4] = -1; records[5 * 4] = float.NaN; records[6 * 4] = float.PositiveInfinity;
        records[7 * 4] = float.MaxValue;
        using var bins = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        bins.UploadDataImmediate(records, 0, 0, 0, width, 1, 1);
        bins.UploadDataImmediate(new float[width * 4], 0, 0, 1, width, 1, 1);
        bins.UploadDataImmediate(new float[width * 4], 0, 0, 2, width, 1, 1);
        float[] reveal = Enumerable.Range(0, width).SelectMany(i => new[] { i == 8 ? -1f : 0f, 1f, 1f, 1f }).ToArray();
        using var transmission = framework.CreateTexture(width, 1, PixelInternalFormat.Rgba32f, reveal);
        using var zero = framework.CreateTexture(width, 1, PixelInternalFormat.Rgba32f, new float[width * 4]);
        using var bindBins = StateCache.Current.BindTextureScope(TextureTarget.Texture2DArray, 0, bins.TextureId);
        using var bindReveal = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 1, transmission.TextureId);
        using var bindZero = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 2, zero.TextureId);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), 1);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "OITaccumulation"), 0);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "OITreveal"), 1);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "revealage"), 2);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "inGlow"), 2);
        using var output = CreateMRTRenderTarget(width, 1, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba32f);
        framework.RenderQuadTo(program.ProgramId, output, (.125f, .25f, .5f, .75f));
        float[] actual = output[0].ReadPixels();
        for (int column = 0; column < width; column++)
            for (int channel = 0; channel < 4; channel++)
            {
                float expected = column >= 4 ? new[] { .125f, .25f, .5f, .75f }[channel]
                    : channel == 3 ? 1 : column == 0 ? 0 : 65504;
                Assert.Equal(expected, actual[column * 4 + channel]);
            }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>The installed resolve uses a strict cutoff and preserves admitted large numerator/weight ratios.</summary>
    [Fact]
    public void InstalledResolveRetainsStrictCutoffAndFiniteLargeRatio()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        var tree = SyntaxTree.Parse(File.ReadAllText(Path.Combine(root, "assets/game/shaders/transparentcompose.fsh")), GlslSchema.Instance);
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).Single();
        tree.CreateEditor().Replace(main, """
            void main(){
                int column=int(gl_FragCoord.x);
                float denominator=column==0?0.0:column==1?uintBitsToFloat(floatBitsToUint(.0001)-1u):.0001;
                vec3 numerator=column==3?vec3(1e30):vec3(32.0*200.0);
                outColor=vec4(unproject(vec4(numerator,denominator)),.375);
            }
            """).Commit();
        using var program = Link(shaders, tree.ToText());
        using var framework = new ShaderTestFramework();
        using var output = CreateRenderTarget(4, 1, PixelInternalFormat.Rgba32f);
        framework.RenderQuadTo(program.ProgramId, output);
        float[] pixels = output[0].ReadPixels();
        for (int column = 0; column < 4; column++)
        {
            double expected = column < 2 ? 0 : column == 2 ? 6400.0 / .0001f : (double)1e30f / .0001f;
            for (int channel = 0; channel < 3; channel++)
            {
                float actual = pixels[column * 4 + channel];
                Assert.True(float.IsFinite(actual));
                Assert.InRange(Math.Abs(actual - expected), 0, Math.Max(1, expected * 2e-6));
            }
            Assert.Equal(.375f, pixels[column * 4 + 3]);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>The current positive Gaussian buckets do not amplify a straight color when numerator and weight are accumulated together.</summary>
    [Fact]
    public void InstalledBucketsPreserveStraightColorAcrossRepeatedSubmissions()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        string bucket = File.ReadAllText(Path.Combine(root, "assets/game/shaderincludes/oit.fsh"));
        string fragment = "#version 430 core\n#define USEOIT 1\n" + bucket + "\n" + """
            void main(){
                vec4 sums[3]=vec4[3](vec4(0),vec4(0),vec4(0));
                for(int j=0;j<64;j++){
                    OIT(vec4(16000,8000,4000,.75),0.0,float(j)*30.0);
                    sums[0]+=OITaccumulation0;sums[1]+=OITaccumulation1;sums[2]+=OITaccumulation2;
                }
                OITaccumulation0=sums[0];OITaccumulation1=sums[1];OITaccumulation2=sums[2];
            }
            """;
        using var program = Link(shaders, fragment);
        using var framework = new ShaderTestFramework();
        using var output = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(PixelInternalFormat.Rgba32f, 6).ToArray());
        framework.RenderQuadTo(program.ProgramId, output);
        // The numerator exceeds the binary16 ceiling; RGBA32F accumulation must retain
        // it before resolve. Positively weighted alpha divides away the common sum.
        bool requiresFloat32 = false;
        for (int bin = 0; bin < 3; bin++)
        {
            float[] values = output[3 + bin].ReadPixels();
            Assert.All(values, value => Assert.True(float.IsFinite(value) && value >= 0));
            Assert.True(values[3] > .0001f);
            requiresFloat32 |= values[0] > 65504;
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(values[channel] / values[3], (16000f / (1 << channel)) * .999998f, (16000f / (1 << channel)) * 1.000002f);
        }
        Assert.True(requiresFloat32);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Links a minimal full-screen interface while keeping installed fragment arithmetic intact.</summary>
    private static GpuProgramObject Link(TerrainShaderTestFixture shaders, string fragment)
    {
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            layout(location=0) in vec2 position;
            out vec2 v_texcoord;
            void main(){gl_Position=vec4(position,0,1);v_texcoord=vec2(.5);}
            """);
        return GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, shaders.Compile(ShaderType.FragmentShader, fragment)));
    }
    #endregion
}
