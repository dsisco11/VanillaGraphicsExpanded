using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies the shipped Jacobi shader against independent stencil and convergence oracles.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrPoissonJacobiConvergenceTests : RenderTestBase
{


    private readonly HeadlessGLFixture fixture;

    #region Public API
    /// <summary>Creates this integration suite over the shared headless OpenGL fixture.</summary>
    public PbrPoissonJacobiConvergenceTests(HeadlessGLFixture fixture) : base(fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Provides the preserved zero, sine, and deterministic-noise convergence inputs.</summary>
    public static IEnumerable<object[]> ZeroMeanRhsCases()
    {
        yield return new object[] { "zero", BuildZeroMeanRhs(32, 32, RhsKind.Zero) };
        yield return new object[] { "sine", BuildZeroMeanRhs(32, 32, RhsKind.SineMix) };
        yield return new object[] { "noise", BuildZeroMeanRhs(32, 32, RhsKind.DeterministicNoise) };
    }

    /// <summary>Provides the original one-step stencil input combinations.</summary>
    public static IEnumerable<object[]> JacobiOneStepCases()
    {
        yield return new object[] { "b_zero_h_noise", BuildJacobiStepCase(8, 8, bKind: RhsKind.Zero, hKind: RhsKind.DeterministicNoise) };
        yield return new object[] { "b_sine_h_zero", BuildJacobiStepCase(8, 8, bKind: RhsKind.SineMix, hKind: RhsKind.Zero) };
        yield return new object[] { "b_checker_h_noise", BuildJacobiStepCase(8, 8, bKind: RhsKind.Checkerboard, hKind: RhsKind.DeterministicNoise) };
    }

    /// <summary>Compares one production Jacobi step against the independent periodic CPU stencil.</summary>
    [Theory]
    [MemberData(nameof(JacobiOneStepCases))]
    public void JacobiShader_OneStep_MatchesCpuStencil(string name, JacobiStepCase input)
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        // Arrange: prepare the shipped Jacobi program, authored source images, and output extent.
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        var program = programs.Create("pbr_heightbake_jacobi");

        using var hTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = input.Width, Height = input.Height, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, input.Width, input.Height), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((input.H).AsSpan()).ToArray())]).RequireResource();
        using var bTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = input.Width, Height = input.Height, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, input.Width, input.Height), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((input.B).AsSpan()).ToArray())]).RequireResource();
        using var outTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, input.Width), Height = System.Math.Max(1, input.Height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        // Bind the authored textures and dimensions through the shipped program's declared inputs.
        program.h = hTex;
        program.b = bTex;
        programs.Parameters.CommonSize = (input.Width, input.Height);
        // Act: run one production pass through the retained executable and RHI pipeline.
        programs.Draw(outTex, program);

        // Assert: compare every output texel with the independent periodic CPU stencil.
        float[] cpu = ComputeJacobiStepCpu(input.H, input.B, input.Width, input.Height);
        ImageAssertions.Comparisons.Compare(outTex, cpu, 1e-4f).AssertEqual();
    }

    /// <summary>Checks convergence across the original zero, sine, and deterministic-noise right-hand sides.</summary>
    [Theory]
    [MemberData(nameof(ZeroMeanRhsCases))]
    public void JacobiSolve_PeriodicPoisson_ReducesResidual_OnMultipleInputs(string name, RhsCase rhs)
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        // Arrange: preserve each authored RHS, zeroed initial solution, and 800 iteration count.
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        var program = programs.Create("pbr_heightbake_jacobi");

        // Keep this reasonably fast; Jacobi is slow but we only need to see clear reduction.
        const int iterations = 800;

        using var bTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = rhs.Width, Height = rhs.Height, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, rhs.Width, rhs.Height), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((rhs.Data).AsSpan()).ToArray())]).RequireResource();
        using var h0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, rhs.Width), Height = System.Math.Max(1, rhs.Height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var h1 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, rhs.Width), Height = System.Math.Max(1, rhs.Height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        ClearR32f(h0, 0f);
        ClearR32f(h1, 0f);

        programs.Parameters.CommonSize = (rhs.Width, rhs.Height);

        // Act: execute the authored ping-pong solve.
        // Jacobi ping-pong
        for (int i = 0; i < iterations; i++)
        {
            DynamicTexture2D src = (i % 2 == 0) ? h0 : h1;
            DynamicTexture2D dst = (i % 2 == 0) ? h1 : h0;

            program.h = src;
            program.b = bTex;
            programs.Draw(dst, program);
        }

        // After an even number of iterations, h0 is the latest; after odd, h1.
        DynamicTexture2D hSolved = (iterations % 2 == 0) ? h0 : h1;
        // Evaluate the independently authored periodic equation over every solved texel.
        var residual = ImageAssertions.Statistics.PeriodicLaplacianResidual(hSolved, rhs.Data);
        residual.AssertFiniteRange();
        var stats = (RmsResidual: (float)Math.Sqrt(residual.SumSquares(0) / (rhs.Width * rhs.Height)),
            RmsB: (float)Math.Sqrt(rhs.Data.Sum(value => (double)(value * value)) / (rhs.Width * rhs.Height)));

        // Assert: preserve absolute validity and the original per-input residual thresholds.
        // Absolute checks.
        Assert.True(stats.RmsB > 1e-6 || name == "zero", $"RHS '{name}' unexpectedly near-zero");
        Assert.InRange(stats.RmsResidual, 0, 1.0f);

        if (name == "zero")
        {
            // If b is zero, Laplacian(h) should be close to zero too.
            Assert.True(stats.RmsResidual < 1e-4f, $"Residual too high for zero RHS: {stats.RmsResidual}");
        }
        else
        {
            // Relative convergence check.
            // Jacobi converges slowly for low frequencies; keep this tolerant but meaningful.
            float rel = stats.RmsB > 1e-8f ? (stats.RmsResidual / stats.RmsB) : stats.RmsResidual;
            Assert.True(rel < 0.25f, $"Jacobi residual too high for '{name}': rel={rel:0.000}, rmsRes={stats.RmsResidual:0.000}, rmsB={stats.RmsB:0.000}");
        }
    }

    /// <summary>Stores one authored periodic right-hand side and its dimensions.</summary>
    public readonly record struct RhsCase(int Width, int Height, float[] Data);

    /// <summary>Stores one authored height/source pair for the one-step stencil comparison.</summary>
    public readonly record struct JacobiStepCase(int Width, int Height, float[] H, float[] B);

    #endregion

    #region Private
    /// <summary>Initializes a scalar solution texture through the RHI clear operation.</summary>
    private void ClearR32f(DynamicTexture2D tex, float value)
    {
        Span<float> clear = stackalloc float[1] { value };
        RHICommandContext.Current.ClearTexture(tex, new(new(), 0, 0, 0, tex.Width, tex.Height),
            new(PixelChannelLayout.Red, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes(clear));
    }

    private enum RhsKind
    {
        Zero,
        Checkerboard,
        SineMix,
        DeterministicNoise,
    }

    /// <summary>Builds the original independent height and right-hand-side input pair.</summary>
    private static JacobiStepCase BuildJacobiStepCase(int width, int height, RhsKind bKind, RhsKind hKind)
    {
        var b = BuildZeroMeanRhs(width, height, bKind).Data;
        var h = BuildZeroMeanRhs(width, height, hKind).Data;
        return new JacobiStepCase(width, height, h, b);
    }

    /// <summary>Evaluates one periodic Jacobi stencil step independently on the CPU.</summary>
    private static float[] ComputeJacobiStepCpu(float[] h, float[] b, int width, int height)
    {
        float[] dst = new float[width * height];

        for (int y = 0; y < height; y++)
        {
            int yUp = (y + 1) % height;
            int yDn = (y - 1 + height) % height;

            for (int x = 0; x < width; x++)
            {
                int xRt = (x + 1) % width;
                int xLt = (x - 1 + width) % width;

                float hL = h[y * width + xLt];
                float hR = h[y * width + xRt];
                float hD = h[yDn * width + x];
                float hU = h[yUp * width + x];

                dst[y * width + x] = (hL + hR + hD + hU - b[y * width + x]) * 0.25f;
            }
        }

        return dst;
    }

    /// <summary>Builds a zero-mean periodic RHS from the preserved deterministic patterns.</summary>
    private static RhsCase BuildZeroMeanRhs(int width, int height, RhsKind kind)
    {
        float[] data = new float[width * height];

        switch (kind)
        {
            case RhsKind.Zero:
                break;

            case RhsKind.Checkerboard:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        data[y * width + x] = (((x + y) & 1) == 0) ? 1f : -1f;
                    }
                }
                break;

            case RhsKind.SineMix:
                for (int y = 0; y < height; y++)
                {
                    float fy = (float)y / height;
                    for (int x = 0; x < width; x++)
                    {
                        float fx = (float)x / width;
                        float v = (float)(Math.Sin(2.0 * Math.PI * fx) + 0.5 * Math.Cos(2.0 * Math.PI * fy));
                        data[y * width + x] = v;
                    }
                }
                break;

            case RhsKind.DeterministicNoise:
                var rng = new Random(12345);
                for (int i = 0; i < data.Length; i++)
                {
                    // Uniform(-1,1)
                    data[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown RHS kind");
        }

        // Enforce solvability for periodic Poisson by removing DC component.
        float mean = 0f;
        for (int i = 0; i < data.Length; i++) mean += data[i];
        mean /= Math.Max(1, data.Length);

        if (mean != 0f)
        {
            for (int i = 0; i < data.Length; i++) data[i] -= mean;
        }

        return new RhsCase(width, height, data);
    }
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("statistics", 0, 0);
        programs.Prepare("statistics-poisson", 0, 0);
    }
    #endregion
}
