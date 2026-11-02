using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Numerics;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies a small current-frame local exposure candidate without installing runtime policy.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
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
        using var result = Pipeline(colors, 16, 8, 1, -logarithm, new(.5f), new(4));
        ImageAssertions.Comparisons.CompareConstant(result[1].Texture,logarithm,tolerance:.00001f,channelMask:1).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[1].Texture,1,channelMask:2).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[2].Texture,colors[0].X,tolerance:Math.Max(1e-10,colors[0].X*.00002),channelMask:1).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[2].Texture,0,tolerance:.00001f,channelMask:8).AssertEqual();
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
        using var result = Pipeline(colors, 16, 8, storage, 13, new(.4f, .7f), new(3, 2));
        double[] logs = colors.Select(Luminance).Select(Math.Log2).ToArray();
        var expectedLogs=new double[colors.Length];
        var expectedBaseValues=new double[colors.Length];
        var expectedStops=new double[colors.Length];
        var expectedGain=new double[colors.Length*3];
        for (int i = 0; i < colors.Length; i++)
        {
            double expectedBase = Filter(logs, 16, 8, i, 1);
            double stops = Stops(expectedBase, 13, new(.4f, .7f), new(3, 2));
            expectedLogs[i]=logs[i];expectedBaseValues[i]=expectedBase;expectedStops[i]=stops;
            for (int channel = 0; channel < 3; channel++)
                expectedGain[i*3+channel]=Math.Pow(2,stops);
        }
        ImageAssertions.Comparisons.CompareComponentDoubles(result[0].Texture,expectedLogs,.000003,1).AssertEqual();
        ImageAssertions.Comparisons.CompareComponentDoubles(result[1].Texture,expectedBaseValues,.000005,1).AssertEqual();
        ImageAssertions.Comparisons.CompareComponentDoubles(result[2].Texture,expectedStops,.000005,8).AssertEqual();
        // Round each quotient to float before the original independent double-reference comparison.
        using var divisors=TestFramework.CreateTexture(16,8,TextureFormat.Rgba32f,colors.SelectMany(c=>new[]{c.X,c.Y,c.Z,1f}).ToArray());
        var transform = ImageAssertions.Projections;
        using var gains=transform.Apply(result[2].Texture, MaterialProjections.LocalExposurePrototypeTests_FilterMatchesPhysicalReferenceAcrossStorageScales,second:divisors);
        ImageAssertions.Comparisons.CompareComponentDoubles(gains,expectedGain,.00002,7).AssertEqual();
    }

    /// <summary>Black, negative, NaN and infinity samples are invalid and acquire no correction or borrowed illumination.</summary>
    [Fact]
    public void InvalidSamplesNeverBecomeLight()
    {
        EnsureShaderTestAvailable();
        Vector3[] colors = [Vector3.Zero, new(-1, 1, 1), new(float.NaN), new(float.PositiveInfinity), new(.001f), new(.002f)];
        using var result = Pipeline(colors, 6, 1, 1, 14, new(.5f), new(4));
        var invalid=new RHITextureRegion(new(0),0,0,0,4,1,1);
        ImageAssertions.Comparisons.CompareConstant(result[0].Texture,0,selection:invalid,channelMask:2).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[1].Texture,0,selection:invalid,channelMask:2).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[2].Texture,0,selection:invalid).AssertEqual();
        Assert.Equal(1,GpuLocalRead.Scalar(result[1].Texture,9));
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
        using var filtered = DrawImage("outColor=vec4(VgeFilterLocalBase(sourceImage,ivec2(gl_FragCoord.xy),1.0),0,1);", 3, 1, mixed);
        Assert.Equal(-16,GpuLocalRead.Scalar(filtered.Texture,0));Assert.Equal(1,GpuLocalRead.Scalar(filtered.Texture,1));
        Assert.Equal(0,GpuLocalRead.Scalar(filtered.Texture,5));Assert.Equal(-16,GpuLocalRead.Scalar(filtered.Texture,8));
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
        using var result = DrawImage("float q=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;float b;bool v=VgeSampleLocalBase(baseImage,vec2(q),-16.0,1.0,b);outColor=vec4(b,v?1:0,0,1);", 6, 1, coordinates, valid);
        ImageAssertions.Predicates.Predicate(result.Texture, MaterialPredicates.LocalExposurePrototypeTests_InvalidCoordinatesAreRejectedBeforeIntegerConversion).AssertEqual();
    }

    /// <summary>Corrupted globalStops or base metadata cannot produce nonfinite correction under identity compression.</summary>
    [Fact]
    public void InvalidCameraInputsMetadataProducesNeutralCorrection()
    {
        EnsureShaderTestAvailable();
        using var metadata = TestFramework.CreateTexture(6, 1, TextureFormat.R32f, [-151, 129, 1e30f, -1e30f, float.NaN, float.PositiveInfinity]);
        using var result = DrawImage("float q=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;outColor=vec4(VgeLocalExposureStops(q,0.0,0.0,vec2(1),vec2(8)),VgeLocalExposureStops(0.0,q,0.0,vec2(.5),vec2(8)),0,1);", 6, 1, metadata);
        ImageAssertions.Comparisons.CompareVector(result.Texture,new(0,0,0,1)).AssertEqual();
    }

    /// <summary>A nonfinite display pivot cannot create local gain even with otherwise valid scene metadata.</summary>
    [Fact]
    public void InvalidDisplayPivotProducesNeutralCorrection()
    {
        EnsureShaderTestAvailable();
        using var metadata=TestFramework.CreateTexture(3,1,TextureFormat.R32f,[float.NaN,float.PositiveInfinity,float.NegativeInfinity]);
        using var result=DrawImage("float q=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;outColor=vec4(VgeLocalExposureStops(-12.0,2.0,q,vec2(.5),vec2(4)),0,0,1);",3,1,metadata);
        ImageAssertions.Comparisons.CompareVector(result.Texture,new(0,0,0,1)).AssertEqual();
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
        using var result = DrawImage("float b=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;float s=VgeLocalExposureStops(b,0.0,0.0,strengths,bounds);outColor=vec4(s,b+s,(b+.25)+s,1);", 9, 1, values, null, 1, 0, new(.5f, .25f), new(3, 2));
        float[] samples = [-100, -4, -1, -.0001f, 0, .0001f, 1, 4, 100];
        var expectedStops=new double[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            expectedStops[i]=Stops(samples[i],0,new(.5f,.25f),new(3,2));
        ImageAssertions.Statistics.Inspect(result.Texture,-2,3,channelMask:1).AssertFiniteRange();
        ImageAssertions.Comparisons.CompareComponentDoubles(result.Texture,expectedStops,.00001,1).AssertEqual();
        ImageAssertions.Predicates.Predicate(result.Texture, MaterialPredicates.LocalExposurePrototypeTests_BoundsAndSmoothJoinPreserveBaseDetail).AssertEqual();
        Assert.InRange(Math.Abs(GpuLocalRead.Scalar(result.Texture,3*4)-GpuLocalRead.Scalar(result.Texture,5*4)),0,.0002);
        using var identity = DrawImage("float b=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).r;outColor=vec4(VgeLocalExposureStops(b,0.0,0.0,vec2(1),vec2(8)),0,0,1);", 9, 1, values);
        ImageAssertions.Comparisons.CompareConstant(identity.Texture,0,channelMask:1).AssertEqual();
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
        using var local = Pipeline(colors, 16, 8, 1, globalStops, new(.5f), new(4));
        Vector4 controls = new(1, 1, 1.111798286f, .143282309f);
        using var lookup = new ColorGradingLut();
        var graded = lookup.Prepare(Programs.Api, controls);
        using var baselineInput = TestFramework.CreateTexture(16, 8, TextureFormat.Rgba32f, colors.SelectMany(c => new[] { c.X, c.Y, c.Z, 1f }).ToArray());
        using var candidateInput = local[2].ToPlane();
        const string body = "vec3 c=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).rgb*64.0;outColor=vec4(texture(gradingLookup,(VgeGradingCoordinate(c)*63.0+.5)/64.0).rgb,1);";
        using var baseline = DrawImage(body, 16, 8, baselineInput, null, grading: graded);
        using var candidate = DrawImage(body, 16, 8, candidateInput, null, grading: graded);
        foreach (int pixel in new[] { 3, 12 })
        {
            // The independent grading reference needs only the two originally nominated RGB tuples.
            float[] corrected=candidateInput.ReadPixelsRegion(pixel,0,1,1);
            Vector3 physical = new(corrected[0],corrected[1],corrected[2]);
            Vector3 expected = FilmicGradingReference.Lookup(physical * 64, controls);
            for (int c = 0; c < 3; c++) Assert.InRange(Math.Abs(GpuLocalRead.Scalar(candidate.Texture,pixel*4+c) - expected[c]), 0, .001f);
            output.WriteLine($"Synthetic {(pixel == 3 ? "terrain" : "lamp")}: stops {corrected[3]:R}.");
        }
        Assert.True(GpuLocalRead.Scalar(candidate.Texture,3*4)>GpuLocalRead.Scalar(baseline.Texture,3*4));
        Assert.True(GpuLocalRead.Scalar(candidate.Texture,12*4)<GpuLocalRead.Scalar(baseline.Texture,12*4));
        Assert.Equal(1, lookup.Generation);
    }
    #endregion

    #endregion
    #region Private
    #region GPU pipeline
    /// <summary>Returns one explicitly local tuple; image-domain callers retain GPU storage instead.</summary>
    private float[] Draw(string body,int width,int height,DynamicTexture2D input,DynamicTexture2D? baseImage=null,
        float storage=1,float globalStops=0,Vector2 strengths=default,Vector2 bounds=default)
    {
        if(width!=1 || height!=1)throw new ArgumentException("Local tuple draws require one texel.");
        using var image=DrawImage(body,width,height,input,baseImage,storage,globalStops,strengths,bounds);
        return image.Texture.ReadPixelsRegion(0,0,1,1);
    }
    /// <summary>Runs log classification, an RG32F base and guided RGB correction on a bounded test image.</summary>
    private GpuImageSet Pipeline(Vector3[] colors, int width, int height, float storage,
        float globalStops, Vector2 strengths, Vector2 bounds)
    {
        using var input = TestFramework.CreateTexture(width, height, TextureFormat.Rgba32f,
            colors.SelectMany(c => new[] { c.X * storage, c.Y * storage, c.Z * storage, 1f }).ToArray());
        using var logs = DrawImage("float q;bool v=VgeLocalLogLuminance(texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).rgb/storage,q);outColor=vec4(q,v?1:0,0,1);", width, height, input, null, storage, format: TextureFormat.Rg32f);
        using var logImage=logs.ToPlane();
        using var filtered = DrawImage("outColor=vec4(VgeFilterLocalBase(sourceImage,ivec2(gl_FragCoord.xy),1.0),0,1);", width, height, logImage, format: TextureFormat.Rg32f);
        using var baseImage=filtered.ToPlane();
        using var color = DrawImage("vec3 c=texelFetch(sourceImage,ivec2(gl_FragCoord.xy),0).rgb/storage;float q,b;float s=0;bool v=VgeLocalLogLuminance(c,q);if(v&&VgeSampleLocalBase(baseImage,gl_FragCoord.xy/vec2(textureSize(sourceImage,0)),q,1.0,b))s=VgeLocalExposureStops(b,globalStops,0.0,strengths,bounds);outColor=vec4(v?c*exp2(s):vec3(0),s);", width, height, input, baseImage, storage, globalStops, strengths, bounds);
        return new([logs.Texture,filtered.Texture,color.Texture]);
    }

    /// <summary>Compiles the shared production helper at GLSL 330 and submits one tiny independent diagnostic pass.</summary>
    private GpuImageSnapshot DrawImage(string body, int width, int height, DynamicTexture2D input, DynamicTexture2D? baseImage = null,
        float storage = 1, float globalStops = 0, Vector2 strengths = default, Vector2 bounds = default,
        TextureFormat format = TextureFormat.Rgba32f, DynamicTexture3D? grading = null)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        string fragment = "#version 330 core\nuniform sampler2D sourceImage;uniform sampler2D baseImage;uniform sampler3D gradingLookup;uniform float storage,globalStops,guidance;uniform vec2 strengths,bounds;out vec4 outColor;\n"
            + Expand(Path.Combine(root, "post_local_exposure.glsl")) + Expand(Path.Combine(root, "pbr_color.glsl"))
            + Expand(Path.Combine(root, "post_filmic.glsl")) + "\nvoid main(){" + body + "}";
        int program = TestShaderCache.CreateGraphics("#version 330 core\nlayout(location=0)in vec2 position;void main(){gl_Position=vec4(position,0,1);}", fragment);
        try
        {
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
            return new(target[0]);
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); GL.DeleteProgram(program); }
    }

    #endregion
    #region Shader preparation
    /// <summary>Expands source-authored guarded imports for the diagnostic compiler.</summary>
    private static string Expand(string path) => Regex.Replace(TestAssetSnapshot.ReadSourceText(path), "@import \"([^\"]+)\"",
        match => Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups[1].Value))));

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

    #endregion
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-material", 0, 0);
        programs.Prepare("projection-material", 0, 0);
    }
    #endregion
}
