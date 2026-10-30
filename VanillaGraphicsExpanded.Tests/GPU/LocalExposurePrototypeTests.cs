using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Numerics;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies a small current-frame local exposure candidate without installing runtime policy.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LocalExposurePrototypeTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    #region Physical inputs
    /// <summary>Uniform scenes and identity compression remain independent of arbitrary brightness floors.</summary>
    [Theory]
    [InlineData(-24f)]
    [InlineData(-12f)]
    [InlineData(8f)]
    public void UniformFramesRemainIdentity(float logarithm)
    {
        EnsureShaderTestAvailable();
        Vector3[] colors = Enumerable.Repeat(new Vector3(MathF.Pow(2, logarithm)), 16 * 8).ToArray();
        var result = Pipeline(colors, 16, 8, 1, -logarithm, new(.5f), new(4));
        for (int i = 0; i < colors.Length; i++)
        {
            Assert.InRange(Math.Abs(result.Base[i * 2] - logarithm), 0, .00001f);
            Assert.Equal(1, result.Base[i * 2 + 1]);
            Assert.InRange(Math.Abs(result.Color[i * 4] - colors[i].X), 0, Math.Max(1e-10, colors[i].X * .00002));
            Assert.InRange(Math.Abs(result.Color[i * 4 + 3]), 0, .00001f);
        }
    }

    /// <summary>Filtering and correction match an independent double reference at borders and three storage scales.</summary>
    [Theory]
    [InlineData(.000244140625f)]
    [InlineData(1f)]
    [InlineData(64f)]
    public void FilterMatchesPhysicalReferenceAcrossStorageScales(float storage)
    {
        EnsureShaderTestAvailable();
        Vector3[] colors = Enumerable.Range(0, 128).Select(i => new Vector3(.00003f * MathF.Pow(2, (i % 16) * .15f), .00002f, .00001f)).ToArray();
        var result = Pipeline(colors, 16, 8, storage, 13, new(.4f, .7f), new(3, 2));
        double[] logs = colors.Select(Luminance).Select(Math.Log2).ToArray();
        for (int i = 0; i < colors.Length; i++)
        {
            double expectedBase = Filter(logs, 16, 8, i, 1);
            double stops = Stops(expectedBase, 13, new(.4f, .7f), new(3, 2));
            Assert.InRange(Math.Abs(result.Log[i * 2] - logs[i]), 0, .000003);
            Assert.InRange(Math.Abs(result.Base[i * 2] - expectedBase), 0, .000005);
            Assert.InRange(Math.Abs(result.Color[i * 4 + 3] - stops), 0, .000005);
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(Math.Abs(result.Color[i * 4 + channel] / colors[i][channel] - Math.Pow(2, stops)), 0, .00002);
        }
    }

    /// <summary>Black, negative, NaN and infinity samples are invalid and acquire no correction or borrowed illumination.</summary>
    [Fact]
    public void InvalidSamplesNeverBecomeLight()
    {
        EnsureShaderTestAvailable();
        Vector3[] colors = [Vector3.Zero, new(-1, 1, 1), new(float.NaN), new(float.PositiveInfinity), new(.001f), new(.002f)];
        var result = Pipeline(colors, 6, 1, 1, 14, new(.5f), new(4));
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(0, result.Log[i * 2 + 1]);
            Assert.Equal(0, result.Base[i * 2 + 1]);
            Assert.All(result.Color.Skip(i * 4).Take(4), value => Assert.Equal(0, value));
        }
        Assert.Equal(1, result.Base[9]);
    }

    #endregion
    #region Sampling and metadata admission
    /// <summary>Full-resolution guidance rejects an isolated bright source and normalizes reconstruction at clamped borders.</summary>
    [Fact]
    public void GuidedReconstructionRejectsBrightLeakage()
    {
        EnsureShaderTestAvailable();
        using var texture = TestFramework.CreateTexture(2, 1, TextureFormat.Rg32f, [-16, 1, 8, 1]);
        string body = "float b; bool valid=VgeSampleLocalBase(sourceImage,vec2(.5),guidance,1.0,b); outColor=vec4(b,valid?1:0,0,1);";
        foreach (float guidance in new[] { -16f, 8f })
        {
            float[] result = Draw(body, 1, 1, texture, null, 1, guidance, default, default);
            Assert.InRange(Math.Abs(result[0] - guidance), 0, .00001f);
            Assert.Equal(1, result[1]);
        }
        float[] border = Draw("float b; bool v=VgeSampleLocalBase(sourceImage,vec2(0),-16.0,1.0,b); outColor=vec4(b,v?1:0,0,1);", 1, 1, texture);
        Assert.Equal(-16, border[0]); Assert.Equal(1, border[1]);
        using var missing = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, [0, 0]);
        float[] invalid = Draw(body, 1, 1, missing, null, 1, -16);
        Assert.Equal(0, invalid[1]); Assert.Equal(-16, invalid[0]);
    }

    /// <summary>Corrupted finite or nonfinite base metadata never contaminates neighboring valid samples.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1e30f)]
    public void CorruptedBaseMetadataIsRejected(float corrupted)
    {
        EnsureShaderTestAvailable();
        using var mixed = TestFramework.CreateTexture(3, 1, TextureFormat.Rg32f, [-16, 1, corrupted, 1, -16, 1]);
        float[] filtered = Draw("outColor=vec4(VgeFilterLocalBase(sourceImage,ivec2(gl_FragCoord.xy),1.0),0,1);", 3, 1, mixed);
        Assert.Equal(-16, filtered[0]); Assert.Equal(1, filtered[1]);
        Assert.Equal(0, filtered[5]); Assert.Equal(-16, filtered[8]);
        using var missing = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, [corrupted, 1]);
        float[] guided = Draw("float b;bool v=VgeSampleLocalBase(sourceImage,vec2(.5),-16.0,1.0,b);outColor=vec4(b,v?1:0,0,1);", 1, 1, missing);
        Assert.Equal(-16, guided[0]); Assert.Equal(0, guided[1]);
    }

    /// <summary>Invalid candidate widths produce a missing base and neutral reconstruction rather than undefined weights.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidRangeWidthsAreNeutral(float width)
    {
        EnsureShaderTestAvailable();
        using var valid = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, [-16, 1]);
        float[] result = Draw("vec2 f=VgeFilterLocalBase(sourceImage,ivec2(0),storage);float b;bool v=VgeSampleLocalBase(sourceImage,vec2(.5),-16.0,storage,b);outColor=vec4(f,b,v?1:0);", 1, 1, valid, storage: width);
        Assert.Equal(new[] { 0f, 0f, -16f, 0f }, result);
        float[] pixel = Draw("outColor=vec4(VgeFilterLocalBase(sourceImage,ivec2(-1),1.0),0,1);", 1, 1, valid);
        Assert.Equal(new[] { 0f, 0f, 0f, 1f }, pixel);
    }

    /// <summary>Malformed UV coordinates cannot overflow integer conversion or borrow edge illumination.</summary>
    [Fact]
    public void InvalidCoordinatesAreRejectedBeforeIntegerConversion()
    {
        EnsureShaderTestAvailable();
        using var coordinates = TestFramework.CreateTexture(6, 1, TextureFormat.R32f, [-1, .5f, 2, 1e30f, float.NaN, float.PositiveInfinity]);
        using var valid = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, [-16, 1]);
        float[] result = Draw("float q=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;float b;bool v=VgeSampleLocalBase(baseImage,vec2(q),-16.0,1.0,b);outColor=vec4(b,v?1:0,0,1);", 6, 1, coordinates, valid);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(-16, result[i * 4]);
            Assert.Equal(i == 1 ? 1 : 0, result[i * 4 + 1]);
        }
    }

    /// <summary>Corrupted globalStops or base metadata cannot produce nonfinite correction under identity compression.</summary>
    [Fact]
    public void InvalidCameraInputsMetadataProducesNeutralCorrection()
    {
        EnsureShaderTestAvailable();
        using var metadata = TestFramework.CreateTexture(6, 1, TextureFormat.R32f, [-151, 129, 1e30f, -1e30f, float.NaN, float.PositiveInfinity]);
        float[] result = Draw("float q=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;outColor=vec4(VgeLocalExposureStops(q,0.0,0.0,vec2(1),vec2(8)),VgeLocalExposureStops(0.0,q,0.0,vec2(.5),vec2(8)),0,1);", 6, 1, metadata);
        for (int i = 0; i < 6; i++) Assert.Equal(new[] { 0f, 0f, 0f, 1f }, result.Skip(i * 4).Take(4));
    }

    /// <summary>A nonfinite display pivot cannot create local gain even with otherwise valid scene metadata.</summary>
    [Fact]
    public void InvalidDisplayPivotProducesNeutralCorrection()
    {
        EnsureShaderTestAvailable();
        using var metadata=TestFramework.CreateTexture(3,1,TextureFormat.R32f,[float.NaN,float.PositiveInfinity,float.NegativeInfinity]);
        float[] result=Draw("float q=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;outColor=vec4(VgeLocalExposureStops(-12.0,2.0,q,vec2(.5),vec2(4)),0,0,1);",3,1,metadata);
        for(int i=0;i<3;i++)Assert.Equal(new[]{0f,0f,0f,1f},result.Skip(i*4).Take(4));
    }

    /// <summary>Nontrivial guided interpolation agrees with normalized independent double spatial and range weights.</summary>
    [Fact]
    public void GuidedInterpolationMatchesIndependentWeights()
    {
        EnsureShaderTestAvailable();
        using var values = TestFramework.CreateTexture(2, 2, TextureFormat.Rg32f, [-14, 1, -13, 1, -12, 1, -11, 1]);
        float[] actual = Draw("float b;bool v=VgeSampleLocalBase(sourceImage,vec2(.375,.625),-12.5,1.0,b);outColor=vec4(b,v?1:0,0,1);", 1, 1, values);
        double numerator = 0, denominator = 0;
        for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
        {
            double sample = -14 + y * 2 + x, difference = sample + 12.5;
            double weight = (x == 0 ? .75 : .25) * (y == 0 ? .25 : .75) * Math.Exp(-difference * difference / 2);
            numerator += sample * weight; denominator += weight;
        }
        Assert.InRange(Math.Abs(actual[0] - numerator / denominator), 0, .000003);
        Assert.Equal(1, actual[1]);
    }

    #endregion
    #region Compression and display
    /// <summary>Corrections preserve the detail residual, obey independent bounds and join continuously across globalStops.</summary>
    [Fact]
    public void BoundsAndSmoothJoinPreserveBaseDetail()
    {
        EnsureShaderTestAvailable();
        using var values = TestFramework.CreateTexture(9, 1, TextureFormat.R32f, [-100, -4, -1, -.0001f, 0, .0001f, 1, 4, 100]);
        float[] result = Draw("float b=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;float s=VgeLocalExposureStops(b,0.0,0.0,strengths,bounds);outColor=vec4(s,b+s,(b+.25)+s,1);", 9, 1, values, null, 1, 0, new(.5f, .25f), new(3, 2));
        float[] samples = [-100, -4, -1, -.0001f, 0, .0001f, 1, 4, 100];
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.InRange(result[i * 4], -2, 3);
            Assert.InRange(Math.Abs(result[i * 4] - Stops(samples[i], 0, new(.5f, .25f), new(3, 2))), 0, .00001);
            Assert.InRange(Math.Abs(result[i * 4 + 2] - result[i * 4 + 1] - .25), 0, .00001);
        }
        Assert.InRange(Math.Abs(result[3 * 4] - result[5 * 4]), 0, .0002);
        float[] identity = Draw("float b=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;outColor=vec4(VgeLocalExposureStops(b,0.0,0.0,vec2(1),vec2(8)),0,0,1);", 9, 1, values);
        for (int i = 0; i < samples.Length; i++) Assert.Equal(0, identity[i * 4]);
        float[] hardLimit = Draw("outColor=vec4(VgeLocalExposureStops(-100.0,0.0,0.0,vec2(0),vec2(100)),VgeLocalExposureStops(100.0,0.0,0.0,vec2(0),vec2(100)),0,1);", 1, 1, values);
        Assert.Equal(8, hardLimit[0]); Assert.Equal(-8, hardLimit[1]);
    }

    /// <summary>A synthetic 434-to-one lamp/terrain scene demonstrates bounded display compression with the unchanged cached LUT.</summary>
    [Fact]
    public void SyntheticLampTerrainComparisonUsesExistingFilmicLookup()
    {
        EnsureShaderTestAvailable();
        Vector3 terrain = new(.00006592794125f, .0000526507864f, .00001556631946f), lamp = terrain * 434;
        Vector3[] colors = Enumerable.Range(0, 128).Select(i => i % 16 < 8 ? terrain : lamp).ToArray();
        float globalStops = -(float)((Math.Log2(Luminance(terrain)) + Math.Log2(Luminance(lamp))) / 2);
        var local = Pipeline(colors, 16, 8, 1, globalStops, new(.5f), new(4));
        Vector4 controls = new(1, 1, 1.111798286f, .143282309f);
        using var lookup = new ColorGradingLut();
        var graded = lookup.Prepare(Programs.Api, controls);
        using var baselineInput = TestFramework.CreateTexture(16, 8, TextureFormat.Rgba32f, colors.SelectMany(c => new[] { c.X, c.Y, c.Z, 1f }).ToArray());
        using var candidateInput = TestFramework.CreateTexture(16, 8, TextureFormat.Rgba32f, local.Color);
        const string body = "vec3 c=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).rgb*64.0;outColor=vec4(texture(gradingLookup,(VgeGradingCoordinate(c)*63.0+.5)/64.0).rgb,1);";
        float[] baseline = Draw(body, 16, 8, baselineInput, null, grading: graded);
        float[] candidate = Draw(body, 16, 8, candidateInput, null, grading: graded);
        foreach (int pixel in new[] { 3, 12 })
        {
            Vector3 physical = new(local.Color[pixel * 4], local.Color[pixel * 4 + 1], local.Color[pixel * 4 + 2]);
            Vector3 expected = FilmicGradingReference.Lookup(physical * 64, controls);
            for (int c = 0; c < 3; c++) Assert.InRange(Math.Abs(candidate[pixel * 4 + c] - expected[c]), 0, .001f);
            output.WriteLine($"Synthetic {(pixel == 3 ? "terrain" : "lamp")}: baseline codes {Codes(baseline, pixel)}; local codes {Codes(candidate, pixel)}; stops {local.Color[pixel * 4 + 3]:R}.");
        }
        Assert.True(candidate[3 * 4] > baseline[3 * 4]);
        Assert.True(candidate[12 * 4] < baseline[12 * 4]);
        Assert.Equal(1, lookup.Generation);
    }
    #endregion

    #endregion
    #region Private
    #region GPU pipeline
    /// <summary>Runs log classification, an RG32F base and guided RGB correction on a bounded test image.</summary>
    private (float[] Log, float[] Base, float[] Color) Pipeline(Vector3[] colors, int width, int height, float storage,
        float globalStops, Vector2 strengths, Vector2 bounds)
    {
        using var input = TestFramework.CreateTexture(width, height, TextureFormat.Rgba32f,
            colors.SelectMany(c => new[] { c.X * storage, c.Y * storage, c.Z * storage, 1f }).ToArray());
        float[] logs = Draw("float q;bool v=VgeLocalLogLuminance(texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).rgb/storage,q);outColor=vec4(q,v?1:0,0,1);", width, height, input, null, storage, format: TextureFormat.Rg32f);
        using var logImage = TestFramework.CreateTexture(width, height, TextureFormat.Rg32f, logs);
        float[] filtered = Draw("outColor=vec4(VgeFilterLocalBase(sourceImage,ivec2(gl_FragCoord.xy),1.0),0,1);", width, height, logImage, format: TextureFormat.Rg32f);
        using var baseImage = TestFramework.CreateTexture(width, height, TextureFormat.Rg32f, filtered);
        float[] color = Draw("vec3 c=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).rgb/storage;float q,b;float s=0;bool v=VgeLocalLogLuminance(c,q);if(v&&VgeSampleLocalBase(baseImage,gl_FragCoord.xy/vec2(textureSize(sourceImage,0)),q,1.0,b))s=VgeLocalExposureStops(b,globalStops,0.0,strengths,bounds);outColor=vec4(v?c*exp2(s):vec3(0),s);", width, height, input, baseImage, storage, globalStops, strengths, bounds);
        return (logs, filtered, color);
    }

    /// <summary>Compiles the shared production helper at GLSL 330 and submits one tiny independent diagnostic pass.</summary>
    private float[] Draw(string body, int width, int height, DynamicTexture2D input, DynamicTexture2D? baseImage = null,
        float storage = 1, float globalStops = 0, Vector2 strengths = default, Vector2 bounds = default,
        TextureFormat format = TextureFormat.Rgba32f, DynamicTexture3D? grading = null)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        string fragment = "#version 330 core\nuniform sampler2D sourceImage;uniform sampler2D baseImage;uniform sampler3D gradingLookup;uniform float storage,globalStops,guidance;uniform vec2 strengths,bounds;out vec4 outColor;\n"
            + Expand(Path.Combine(root, "post_local_exposure.glsl")) + Expand(Path.Combine(root, "pbr_color.glsl"))
            + Expand(Path.Combine(root, "post_filmic.glsl")) + "\nvoid main(){" + body + "}";
        int vertex = Compile(ShaderType.VertexShader, "#version 330 core\nlayout(location=0)in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        int shader = Compile(ShaderType.FragmentShader, fragment), program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, shader); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var sourceScope = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, input.TextureId);
            using var baseScope = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 1, baseImage?.TextureId ?? input.TextureId);
            using var gradeScope = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 2, grading?.TextureId ?? 0);
            // These raw GLSL fixtures use each texture's authored filter, independently of inherited sampler objects.
            using var sourceSampler = OpenGlStateCache.Current.BindSamplerScope(0, 0);
            using var baseSampler = OpenGlStateCache.Current.BindSamplerScope(1, 0);
            using var gradeSampler = OpenGlStateCache.Current.BindSamplerScope(2, 0);
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, program);
            GL.Uniform1(GL.GetUniformLocation(program, "sourceImage"), 0); GL.Uniform1(GL.GetUniformLocation(program, "baseImage"), 1);
            GL.Uniform1(GL.GetUniformLocation(program, "gradingLookup"), 2);
            GL.Uniform1(GL.GetUniformLocation(program, "storage"), storage);
            GL.Uniform1(GL.GetUniformLocation(program, "globalStops"), globalStops); GL.Uniform1(GL.GetUniformLocation(program, "guidance"), globalStops);
            GL.Uniform2(GL.GetUniformLocation(program, "strengths"), strengths.X, strengths.Y);
            GL.Uniform2(GL.GetUniformLocation(program, "bounds"), bounds.X, bounds.Y);
            using var target = TestFramework.CreateTestGBuffer(width, height, format);
            TestFramework.RenderQuadTo(program, target);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            return target[0].ReadPixels();
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); GL.DeleteProgram(program); GL.DeleteShader(vertex); GL.DeleteShader(shader); }
    }

    #endregion
    #region Shader preparation
    /// <summary>Expands source-authored guarded imports for the diagnostic compiler.</summary>
    private static string Expand(string path) => Regex.Replace(File.ReadAllText(path), "@import \"([^\"]+)\"",
        match => Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups[1].Value))));

    /// <summary>Checks compilation at the draw baseline rather than silently enabling newer language features.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type); GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        string error = GL.GetShaderInfoLog(shader);
        if (success == 0) GL.DeleteShader(shader);
        Assert.True(success != 0, error); return shader;
    }

    #endregion
    #region Numerical references
    /// <summary>Computes physical luminance independently in double precision.</summary>
    private static double Luminance(Vector3 c) => .2126 * c.X + .7152 * c.Y + .0722 * c.Z;

    /// <summary>Evaluates the normalized truncated bilateral footprint without reproducing shader float arithmetic.</summary>
    private static double Filter(double[] logs, int width, int height, int pixel, double range)
    {
        int cx = pixel % width, cy = pixel / width;
        double numerator = 0, denominator = 0;
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(height - 1, cy + 1); y++)
            for (int x = Math.Max(0, cx - 1); x <= Math.Min(width - 1, cx + 1); x++)
            {
                double difference = (logs[y * width + x] - logs[pixel]) / range;
                double weight = (x == cx ? 2 : 1) * (y == cy ? 2 : 1) * Math.Exp(-difference * difference / 2);
                numerator += logs[y * width + x] * weight; denominator += weight;
            }
        return numerator / denominator;
    }

    /// <summary>Independently evaluates piecewise smooth base compression and its engineering bounds.</summary>
    private static double Stops(double b, double globalStops, Vector2 strength, Vector2 bounds)
    {
        double relative = b + globalStops, t = Math.Clamp((relative + 1) / 2, 0, 1);
        double k = strength.X + (strength.Y - strength.X) * t * t * (3 - 2 * t);
        return Math.Clamp((k - 1) * relative, -bounds.Y, bounds.X);
    }

    /// <summary>Reports rounded normalized output codes without claiming full final-display quantization.</summary>
    private static string Codes(float[] data, int pixel) => string.Join(",", data.Skip(pixel * 4).Take(3).Select(v => Math.Round(v * 255)));
    #endregion
    #endregion
}
