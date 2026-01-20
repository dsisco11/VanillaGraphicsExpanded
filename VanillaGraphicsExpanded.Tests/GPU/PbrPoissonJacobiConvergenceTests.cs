using System;
using System.Collections.Generic;
using System.IO;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrPoissonJacobiConvergenceTests : RenderTestBase
{
    private readonly HeadlessGLFixture fixture;

    public PbrPoissonJacobiConvergenceTests(HeadlessGLFixture fixture) : base(fixture)
    {
        this.fixture = fixture;
    }

    public static IEnumerable<object[]> ZeroMeanRhsCases()
    {
        yield return new object[] { "zero", BuildZeroMeanRhs(32, 32, RhsKind.Zero) };
        yield return new object[] { "sine", BuildZeroMeanRhs(32, 32, RhsKind.SineMix) };
        yield return new object[] { "noise", BuildZeroMeanRhs(32, 32, RhsKind.DeterministicNoise) };
    }

    public static IEnumerable<object[]> JacobiOneStepCases()
    {
        yield return new object[] { "b_zero_h_noise", BuildJacobiStepCase(8, 8, bKind: RhsKind.Zero, hKind: RhsKind.DeterministicNoise) };
        yield return new object[] { "b_sine_h_zero", BuildJacobiStepCase(8, 8, bKind: RhsKind.SineMix, hKind: RhsKind.Zero) };
        yield return new object[] { "b_checker_h_noise", BuildJacobiStepCase(8, 8, bKind: RhsKind.Checkerboard, hKind: RhsKind.DeterministicNoise) };
    }

    [Theory]
    [MemberData(nameof(JacobiOneStepCases))]
    public void JacobiShader_OneStep_MatchesCpuStencil(string name, JacobiStepCase input)
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.SkipWhen(true, "Test shader assets not found in output directory.");
        }

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var progResult = helper.CompileAndLink(
            vertexFilename: "pbr_heightbake_fullscreen.vsh",
            fragmentFilename: "pbr_heightbake_jacobi.fsh");
        Assert.True(progResult.IsSuccess, progResult.ErrorMessage);

        int programId = progResult.ProgramId;
        Assert.True(programId > 0);

        using var hTex = DynamicTexture2D.CreateWithData(input.Width, input.Height, PixelInternalFormat.R32f, input.H, TextureFilterMode.Nearest);
        using var bTex = DynamicTexture2D.CreateWithData(input.Width, input.Height, PixelInternalFormat.R32f, input.B, TextureFilterMode.Nearest);
        using var outTex = DynamicTexture2D.Create(input.Width, input.Height, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        // GPU step
        int vao = GL.GenVertexArray();
        GL.BindVertexArray(vao);

        using (var fbo = GBuffer.CreateSingle(outTex, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create FBO"))
        {
            fbo.Bind();
            GL.Viewport(0, 0, input.Width, input.Height);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.ColorMask(true, true, true, true);

            GL.UseProgram(programId);

            hTex.Bind(0);
            bTex.Bind(1);
            GL.Uniform1(Uniform(programId, "u_h"), 0);
            GL.Uniform1(Uniform(programId, "u_b"), 1);
            GL.Uniform2(Uniform(programId, "u_size"), input.Width, input.Height);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            GL.UseProgram(0);
            GBuffer.Unbind();
        }

        GL.BindVertexArray(0);
        GL.DeleteVertexArray(vao);

        float[] gpu = outTex.ReadPixels();
        float[] cpu = ComputeJacobiStepCpu(input.H, input.B, input.Width, input.Height);

        Assert.Equal(cpu.Length, gpu.Length);

        // Tight epsilon: this is one stencil step and should match very closely.
        const float eps = 1e-4f;
        for (int i = 0; i < cpu.Length; i++)
        {
            float d = MathF.Abs(cpu[i] - gpu[i]);
            Assert.True(d <= eps, $"{name}: mismatch at i={i}, cpu={cpu[i]}, gpu={gpu[i]}, d={d}");
        }
    }

    [Theory]
    [MemberData(nameof(ZeroMeanRhsCases))]
    public void JacobiSolve_PeriodicPoisson_ReducesResidual_OnMultipleInputs(string name, RhsCase rhs)
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.SkipWhen(true, "Test shader assets not found in output directory.");
        }

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var progResult = helper.CompileAndLink(
            vertexFilename: "pbr_heightbake_fullscreen.vsh",
            fragmentFilename: "pbr_heightbake_jacobi.fsh");
        Assert.True(progResult.IsSuccess, progResult.ErrorMessage);

        int programId = progResult.ProgramId;
        Assert.True(programId > 0);

        // Keep this reasonably fast; Jacobi is slow but we only need to see clear reduction.
        const int iterations = 800;

        using var bTex = DynamicTexture2D.CreateWithData(rhs.Width, rhs.Height, PixelInternalFormat.R32f, rhs.Data, TextureFilterMode.Nearest);
        using var h0 = DynamicTexture2D.Create(rhs.Width, rhs.Height, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var h1 = DynamicTexture2D.Create(rhs.Width, rhs.Height, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        ClearR32f(h0, 0f);
        ClearR32f(h1, 0f);

        int vao = GL.GenVertexArray();
        GL.BindVertexArray(vao);

        // Jacobi ping-pong
        for (int i = 0; i < iterations; i++)
        {
            DynamicTexture2D src = (i % 2 == 0) ? h0 : h1;
            DynamicTexture2D dst = (i % 2 == 0) ? h1 : h0;

            using var fbo = GBuffer.CreateSingle(dst, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create FBO");

            fbo.Bind();
            GL.Viewport(0, 0, rhs.Width, rhs.Height);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.ColorMask(true, true, true, true);

            GL.UseProgram(programId);

            src.Bind(0);
            bTex.Bind(1);

            GL.Uniform1(Uniform(programId, "u_h"), 0);
            GL.Uniform1(Uniform(programId, "u_b"), 1);
            GL.Uniform2(Uniform(programId, "u_size"), rhs.Width, rhs.Height);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            GL.UseProgram(0);
            GBuffer.Unbind();
        }

        GL.BindVertexArray(0);
        GL.DeleteVertexArray(vao);

        // After an even number of iterations, h0 is the latest; after odd, h1.
        DynamicTexture2D hSolved = (iterations % 2 == 0) ? h0 : h1;
        float[] h = hSolved.ReadPixels();

        // Compute residual r = Laplacian(h) - b, using the same periodic stencil as the shader.
        var stats = ComputeResidualStats(h, rhs.Data, rhs.Width, rhs.Height);

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

    private static int Uniform(int programId, string name)
    {
        int loc = GL.GetUniformLocation(programId, name);
        Assert.True(loc >= 0, $"Missing uniform '{name}'");
        return loc;
    }

    private void ClearR32f(DynamicTexture2D tex, float value)
    {
        using var fbo = GBuffer.CreateSingle(tex, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create FBO");
        fbo.Bind();
        GL.Viewport(0, 0, tex.Width, tex.Height);
        GL.Disable(EnableCap.Blend);
        GL.ClearColor(value, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GBuffer.Unbind();
    }

    private static (float RmsResidual, float RmsB) ComputeResidualStats(float[] h, float[] b, int width, int height)
    {
        double sumRes2 = 0;
        double sumB2 = 0;

        int idx = 0;
        for (int y = 0; y < height; y++)
        {
            int yUp = (y + 1) % height;
            int yDn = (y - 1 + height) % height;

            for (int x = 0; x < width; x++, idx++)
            {
                int xRt = (x + 1) % width;
                int xLt = (x - 1 + width) % width;

                float hC = h[idx];
                float hL = h[y * width + xLt];
                float hR = h[y * width + xRt];
                float hD = h[yDn * width + x];
                float hU = h[yUp * width + x];

                float lap = (hL + hR + hD + hU - 4f * hC);
                float res = lap - b[idx];

                sumRes2 += res * res;
                sumB2 += b[idx] * b[idx];
            }
        }

        float rmsRes = (float)Math.Sqrt(sumRes2 / (width * height));
        float rmsB = (float)Math.Sqrt(sumB2 / (width * height));
        return (rmsRes, rmsB);
    }

    private enum RhsKind
    {
        Zero,
        Checkerboard,
        SineMix,
        DeterministicNoise,
    }

    public readonly record struct RhsCase(int Width, int Height, float[] Data);

    public readonly record struct JacobiStepCase(int Width, int Height, float[] H, float[] B);

    private static JacobiStepCase BuildJacobiStepCase(int width, int height, RhsKind bKind, RhsKind hKind)
    {
        var b = BuildZeroMeanRhs(width, height, bKind).Data;
        var h = BuildZeroMeanRhs(width, height, hKind).Data;
        return new JacobiStepCase(width, height, h, b);
    }

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
}
