using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates geometric angular integration independently from sample confidence and missing support.</summary>
public partial class LumOnProbeAtlasProjectSh9FunctionalTests
{
    #region Angular invariants
    /// <summary>Positive confidence patterns cannot create directional bands in constant LDR or representable HDR radiance.</summary>
    [Theory]
    [InlineData(.0009765625f)] [InlineData(1f)] [InlineData(1024f)]
    public void ConstantRadiancePreservesEveryNormalAcrossConfidencePatterns(float scale)
    {
        EnsureShaderTestAvailable();
        var radiance = new Vector3(scale, scale * .25f, scale * 4);
        var uniform = ProjectPattern((_, _) => radiance, (_, _) => 1);
        var varying = ProjectPattern((_, _) => radiance, (x, y) => ((x + 3 * y) % 4) switch { 0 => .0001f, 1 => .01f, 2 => .25f, _ => 1 });
        Assert.Equal(uniform.Take(27), varying.Take(27));
        foreach (var normal in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY,
            Vector3.UnitZ, -Vector3.UnitZ, Vector3.Normalize(new Vector3(1, 2, 3)) })
        {
            var evaluated = EvaluateProjected(varying, normal);
            var gathered = GatherProjection(varying, normal);
            for (int channel = 0; channel < 3; channel++)
                {
                    Assert.InRange(Math.Abs(evaluated[channel] - radiance[channel]), 0, Math.Max(1e-7f, radiance[channel] * .0015f));
                    Assert.InRange(Math.Abs(gathered[channel] - radiance[channel]), 0, Math.Max(1e-7f, radiance[channel] * .0015f));
                }
        }
        for (int coefficient = 3; coefficient < 27; coefficient++)
            Assert.InRange(Math.Abs(varying[coefficient]), 0, Math.Max(1e-7f, scale * .00001f));
        Assert.InRange(varying[27], .01f, .99f);
    }

    /// <summary>Directional and quadratic signals agree with an independent dense UV-area integration of the piecewise-constant cells.</summary>
    [Fact]
    public void AnisotropicCellRadianceMatchesIndependentQuadrature()
    {
        EnsureShaderTestAvailable();
        /// <summary>Authors a directional field with independent first- and second-band color channels.</summary>
        static Vector3 Radiance(int x, int y)
        {
            var direction = DecodeDirection((x + .5) / 8, (y + .5) / 8, out _);
            return new(1 + .6f * direction.X, 2 + .4f * (3 * direction.Z * direction.Z - 1), .3f + .25f * direction.X * direction.Y);
        }
        var projected = ProjectPattern(Radiance, (x, y) => ((x ^ y) & 1) == 0 ? .125f : 1);
        var reference = IntegrateCells(Radiance, (_, _) => true);
        AssertCoefficients(reference, projected);
        Assert.True(EvaluateProjected(projected, Vector3.UnitX).X > EvaluateProjected(projected, -Vector3.UnitX).X + .5f);
        Assert.True(EvaluateProjected(projected, Vector3.UnitZ).Y > EvaluateProjected(projected, Vector3.UnitX).Y + .15f);
    }

    /// <summary>Individual corner, fold and pole cells use their own solid angle rather than equal area or confidence normalization.</summary>
    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(9)] [InlineData(27)] [InlineData(36)] [InlineData(63)]
    public void SparseCellMatchesIndependentQuadrature(int cell)
    {
        EnsureShaderTestAvailable();
        var color = new Vector3(1, .25f, 2);
        var projected = ProjectPattern((x, y) => (y << 3) + x == cell ? color : new Vector3(1024),
            (x, y) => (y << 3) + x == cell ? 1 : 0);
        var reference = IntegrateCells((_, _) => color, (x, y) => (y << 3) + x == cell);
        AssertCoefficients(reference, projected);
        Assert.InRange(projected[27], .001f, .04f);
    }
    #endregion

    #region Missing and dark support
    /// <summary>A missing hemisphere retains half the angular support without promoting supported light into the missing directions.</summary>
    [Fact]
    public void MissingHemisphereDoesNotRenormalizeAvailableLight()
    {
        EnsureShaderTestAvailable();
        var projected = ProjectPattern((x, _) => new Vector3(x < 4 ? 1 : 1024), (x, _) => x < 4 ? 1 : 0);
        Assert.InRange(projected[27], .499f, .501f);
        Assert.InRange(projected[0], 2 * MathF.PI * SH9_C0 - .002f, 2 * MathF.PI * SH9_C0 + .002f);
        Assert.InRange(EvaluateProjected(projected, -Vector3.UnitX).X, .998f, 1.002f);
        Assert.InRange(EvaluateProjected(projected, Vector3.UnitX).X, 0, .002f);
        var gathered = GatherProjection(projected);
        Assert.InRange(gathered.X, .498f, .502f);
        Assert.InRange(gathered.W, .499f, .501f);
    }

    /// <summary>Valid darkness remains confident while missing samples cannot manufacture a confidently resolved black probe.</summary>
    [Theory]
    [InlineData(0f, 1024f, 0f, 0f)]
    [InlineData(1f, 0f, 0f, 1f)]
    [InlineData(.25f, 1f, 1f, .25f)]
    [InlineData(.0001f, 1f, 0f, 0f)]
    public void GatherUsesDirectionalReliability(float confidence, float radiance, float expectedRadiance, float expectedConfidence)
    {
        EnsureShaderTestAvailable();
        var projected = ProjectPattern((_, _) => new Vector3(radiance), (_, _) => confidence);
        Assert.InRange(projected[27], Math.Max(0, confidence - .0005f), confidence + .0005f);
        var gathered = GatherProjection(projected);
        for (int channel = 0; channel < 3; channel++) Assert.InRange(Math.Abs(gathered[channel] - expectedRadiance), 0, .002f);
        Assert.InRange(Math.Abs(gathered.W - expectedConfidence), 0, .002f);
    }
    #endregion

    #region Real shader boundaries
    /// <summary>Projects authored cell values through the real shader and reads the packed coefficients plus reliability.</summary>
    private float[] ProjectPattern(Func<int, int, Vector3> radiance, Func<int, int, float> confidence)
    {
        Assert.Equal(8, VanillaGraphicsExpanded.Rendering.DynamicTexture3D.OctahedralSize);
        var atlas = new float[(AtlasWidth * AtlasHeight) << 2];
        var meta = new float[(AtlasWidth * AtlasHeight) << 1];
        for (int y = 0; y < AtlasHeight; y++) for (int x = 0; x < AtlasWidth; x++)
        {
            int pixel = y * AtlasWidth + x;
            var color = radiance(x & 7, y & 7);
            atlas[pixel << 2] = color.X; atlas[(pixel << 2) + 1] = color.Y; atlas[(pixel << 2) + 2] = color.Z;
            atlas[(pixel << 2) + 3] = MathF.Log(11);
            meta[pixel << 1] = confidence(x & 7, y & 7);
            meta[(pixel << 1) + 1] = BitConverter.UInt32BitsToSingle(1);
        }
        using var anchors = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, CreateProbeAnchors(-1));
        using var atlasTexture = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlas);
        using var metadata = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, meta);
        using var output = TestFramework.CreateTestGBuffer(ProbeGridWidth, ProbeGridHeight, Enumerable.Repeat(PixelInternalFormat.Rgba16f, 7).ToArray());
        var program = CompileProjectShader();
        using var use = program.UseScope();
        SetupProjectUniforms(program);
        program.ScreenProbeAtlas = atlasTexture; program.ScreenProbeAtlasMeta = metadata; program.ProbeAnchorPosition = anchors;
        TestFramework.RenderQuadTo(program, output);
        var result = new float[28];
        for (int target = 0; target < 7; target++) output[target].ReadPixels().AsSpan(0, 4).CopyTo(result.AsSpan(target << 2, 4));
        Assert.All(result, value => Assert.True(float.IsFinite(value)));
        return result;
    }

    /// <summary>Evaluates projected data through the production gather, including its confidence and no-support branches.</summary>
    private Vector4 GatherProjection(float[] coefficients, Vector3? receiverNormal = null)
    {
        var normal = receiverNormal ?? Vector3.UnitY;
        using var packed = TestFramework.CreateTestGBuffer(ProbeGridWidth, ProbeGridHeight, Enumerable.Repeat(PixelInternalFormat.Rgba16f, 7).ToArray());
        for (int target = 0; target < 7; target++)
        {
            var pixels = new float[(ProbeGridWidth * ProbeGridHeight) << 2];
            for (int pixel = 0; pixel < pixels.Length; pixel += 4) coefficients.AsSpan(target << 2, 4).CopyTo(pixels.AsSpan(pixel, 4));
            ((VanillaGraphicsExpanded.Rendering.DynamicTexture2D)packed[target]).UploadDataImmediate(pixels);
        }
        using var anchors = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, CreateProbeAnchors(-1));
        using var normals = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, CreateProbeNormalsEncoded(normal.X, normal.Y, normal.Z));
        using var depth = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, CreateUniformDepthData(ScreenWidth, ScreenHeight, .5f));
        using var guides = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, CreateUniformNormalData(ScreenWidth, ScreenHeight, normal.X, normal.Y, normal.Z));
        using var output = TestFramework.CreateTestGBuffer(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f);
        var program = CompileSh9GatherShader();
        using var use = program.UseScope();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupSh9GatherUniforms(program, identity, identity);
        program.ProbeSh0 = packed[0]; program.ProbeSh1 = packed[1]; program.ProbeSh2 = packed[2]; program.ProbeSh3 = packed[3];
        program.ProbeSh4 = packed[4]; program.ProbeSh5 = packed[5]; program.ProbeSh6 = packed[6];
        program.ProbeAnchorPosition = anchors; program.ProbeAnchorNormal = normals; program.PrimaryDepth = depth.TextureId; program.GBufferNormal = guides.TextureId;
        TestFramework.RenderQuadTo(program, output);
        var result = output[0].ReadPixels();
        return new(result[0], result[1], result[2], result[3]);
    }
    #endregion

    #region Independent numerical reference
    /// <summary>Integrates the UV Jacobian with dense midpoint samples rather than the generator's analytic spherical boundary moments.</summary>
    private static double[] IntegrateCells(Func<int, int, Vector3> radiance, Func<int, int, bool> available)
    {
        const int subdivision = 32;
        var coefficients = new double[27];
        Span<double> basis = stackalloc double[9];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
        {
            if (!available(x, y)) continue;
            var color = radiance(x, y);
            for (int sy = 0; sy < subdivision; sy++) for (int sx = 0; sx < subdivision; sx++)
            {
                var direction = DecodeDirection((x + (sx + .5) / subdivision) / 8, (y + (sy + .5) / subdivision) / 8, out double jacobian);
                FillBasis(direction, basis);
                double area = jacobian / ((subdivision * subdivision) << 6);
                for (int coefficient = 0; coefficient < 9; coefficient++) for (int channel = 0; channel < 3; channel++)
                    coefficients[coefficient * 3 + channel] += color[channel] * basis[coefficient] * area;
            }
        }
        return coefficients;
    }

    /// <summary>Decodes the octahedron and derives its UV-to-sphere area Jacobian independently of the generated table.</summary>
    private static Vector3 DecodeDirection(double u, double v, out double jacobian)
    {
        double x = u * 2 - 1, y = v * 2 - 1, z = 1 - Math.Abs(x) - Math.Abs(y);
        if (z < 0) { double oldX = x; x = (1 - Math.Abs(y)) * (x < 0 ? -1 : 1); y = (1 - Math.Abs(oldX)) * (y < 0 ? -1 : 1); }
        double length = Math.Sqrt(x * x + y * y + z * z);
        jacobian = 4 / (length * length * length);
        return new((float)(x / length), (float)(y / length), (float)(z / length));
    }

    /// <summary>Evaluates the documented real SH basis for numerical integration and diffuse reconstruction.</summary>
    private static void FillBasis(Vector3 direction, Span<double> basis)
    {
        double x = direction.X, y = direction.Y, z = direction.Z;
        basis[0] = .282095; basis[1] = .488603 * y; basis[2] = .488603 * z; basis[3] = .488603 * x;
        basis[4] = 1.092548 * x * y; basis[5] = 1.092548 * y * z; basis[6] = .315392 * (3 * z * z - 1);
        basis[7] = 1.092548 * x * z; basis[8] = .546274 * (x * x - y * y);
    }

    /// <summary>Reconstructs the outgoing diffuse value with the analytic Lambertian band factors.</summary>
    private static Vector3 EvaluateProjected(float[] packed, Vector3 normal)
    {
        Span<double> basis = stackalloc double[9];
        FillBasis(normal, basis);
        var result = Vector3.Zero;
        for (int coefficient = 0; coefficient < 9; coefficient++)
        {
            double convolution = coefficient == 0 ? 1 : coefficient < 4 ? 2.0 / 3 : .25;
            for (int channel = 0; channel < 3; channel++) result[channel] += (float)(packed[coefficient * 3 + channel] * basis[coefficient] * convolution);
        }
        return Vector3.Max(Vector3.Zero, result);
    }

    /// <summary>Allows half-float coefficient storage and the independent midpoint oracle's finite subdivision error.</summary>
    private static void AssertCoefficients(double[] expected, float[] actual)
    {
        for (int coefficient = 0; coefficient < expected.Length; coefficient++)
            Assert.InRange(Math.Abs(actual[coefficient] - expected[coefficient]), 0, .0002 + Math.Abs(expected[coefficient]) * .0015);
    }
    #endregion
}


