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
public sealed class PbrPoissonMultigridVCycleTests : RenderTestBase
{
    private readonly HeadlessGLFixture fixture;

    public PbrPoissonMultigridVCycleTests(HeadlessGLFixture fixture) : base(fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void ResidualShader_MatchesCpuStencil_OnRandomInputs()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        using var helper = CreateHelperOrSkip();

        int progResidual = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_residual.fsh");

        const int w = 8;
        const int h = 8;

        float[] hData = BuildDeterministicNoise(w, h, seed: 101);
        float[] bData = BuildDeterministicNoise(w, h, seed: 202);

        using var hTex = DynamicTexture2D.CreateWithData(w, h, PixelInternalFormat.R32f, hData, TextureFilterMode.Nearest);
        using var bTex = DynamicTexture2D.CreateWithData(w, h, PixelInternalFormat.R32f, bData, TextureFilterMode.Nearest);
        using var rTex = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        RenderTo(rTex, progResidual, () =>
        {
            hTex.Bind(0);
            bTex.Bind(1);
            GL.Uniform1(Uniform(progResidual, "u_h"), 0);
            GL.Uniform1(Uniform(progResidual, "u_b"), 1);
            GL.Uniform2(Uniform(progResidual, "u_size"), w, h);
        });

        float[] gpu = rTex.ReadPixels();
        float[] cpu = ComputeResidualCpu(hData, bData, w, h);

        Assert.Equal(cpu.Length, gpu.Length);

        const float eps = 1e-4f;
        for (int i = 0; i < cpu.Length; i++)
        {
            float d = MathF.Abs(cpu[i] - gpu[i]);
            Assert.True(d <= eps, $"Residual mismatch at i={i}, cpu={cpu[i]}, gpu={gpu[i]}, d={d}");
        }
    }

    [Fact]
    public void RestrictShader_MatchesCpuBoxFilter()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        using var helper = CreateHelperOrSkip();

        int progRestrict = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_restrict.fsh");

        const int fineW = 8;
        const int fineH = 8;
        const int coarseW = 4;
        const int coarseH = 4;

        float[] fine = BuildDeterministicNoise(fineW, fineH, seed: 303);

        using var fineTex = DynamicTexture2D.CreateWithData(fineW, fineH, PixelInternalFormat.R32f, fine, TextureFilterMode.Nearest);
        using var coarseTex = DynamicTexture2D.Create(coarseW, coarseH, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        RenderTo(coarseTex, progRestrict, () =>
        {
            fineTex.Bind(0);
            GL.Uniform1(Uniform(progRestrict, "u_fine"), 0);
            GL.Uniform2(Uniform(progRestrict, "u_fineSize"), fineW, fineH);
            GL.Uniform2(Uniform(progRestrict, "u_coarseSize"), coarseW, coarseH);
        });

        float[] gpu = coarseTex.ReadPixels();
        float[] cpu = ComputeRestrictCpu(fine, fineW, fineH, coarseW, coarseH);

        Assert.Equal(cpu.Length, gpu.Length);

        const float eps = 1e-4f;
        for (int i = 0; i < cpu.Length; i++)
        {
            float d = MathF.Abs(cpu[i] - gpu[i]);
            Assert.True(d <= eps, $"Restrict mismatch at i={i}, cpu={cpu[i]}, gpu={gpu[i]}, d={d}");
        }
    }

    [Fact]
    public void ProlongateAddShader_MatchesCpuBilinearPlusH()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        using var helper = CreateHelperOrSkip();

        int progProlongate = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_prolongate_add.fsh");

        const int fineW = 8;
        const int fineH = 8;
        const int coarseW = 4;
        const int coarseH = 4;

        float[] fineHData = BuildDeterministicNoise(fineW, fineH, seed: 404);
        float[] coarseE = BuildDeterministicNoise(coarseW, coarseH, seed: 505);

        using var fineTex = DynamicTexture2D.CreateWithData(fineW, fineH, PixelInternalFormat.R32f, fineHData, TextureFilterMode.Nearest);
        using var coarseTex = DynamicTexture2D.CreateWithData(coarseW, coarseH, PixelInternalFormat.R32f, coarseE, TextureFilterMode.Nearest);
        using var outTex = DynamicTexture2D.Create(fineW, fineH, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        RenderTo(outTex, progProlongate, () =>
        {
            fineTex.Bind(0);
            coarseTex.Bind(1);
            GL.Uniform1(Uniform(progProlongate, "u_fineH"), 0);
            GL.Uniform1(Uniform(progProlongate, "u_coarseE"), 1);
            GL.Uniform2(Uniform(progProlongate, "u_fineSize"), fineW, fineH);
            GL.Uniform2(Uniform(progProlongate, "u_coarseSize"), coarseW, coarseH);
        });

        float[] gpu = outTex.ReadPixels();
        float[] cpu = ComputeProlongateAddCpu(fineHData, coarseE, fineW, fineH, coarseW, coarseH);

        Assert.Equal(cpu.Length, gpu.Length);

        const float eps = 1e-4f;
        for (int i = 0; i < cpu.Length; i++)
        {
            float d = MathF.Abs(cpu[i] - gpu[i]);
            Assert.True(d <= eps, $"Prolongate mismatch at i={i}, cpu={cpu[i]}, gpu={gpu[i]}, d={d}");
        }
    }

    public static IEnumerable<object[]> VCycleCases()
    {
        yield return new object[] { "sine", BuildZeroMeanRhs(32, 32, RhsKind.SineMix) };
        yield return new object[] { "noise", BuildZeroMeanRhs(32, 32, RhsKind.DeterministicNoise) };
    }

    [Theory]
    [MemberData(nameof(VCycleCases))]
    public void MultigridVCycle_ReducesResidual(string name, RhsCase rhs)
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        using var helper = CreateHelperOrSkip();

        int progJacobi = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_jacobi.fsh");
        int progResidual = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_residual.fsh");
        int progRestrict = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_restrict.fsh");
        int progProlongate = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_prolongate_add.fsh");

        int w = rhs.Width;
        int h = rhs.Height;
        int cw = Math.Max(1, w / 2);
        int ch = Math.Max(1, h / 2);

        using var bFine = DynamicTexture2D.CreateWithData(w, h, PixelInternalFormat.R32f, rhs.Data, TextureFilterMode.Nearest);

        using var h0 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var h1 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        using var resFine = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var bCoarse = DynamicTexture2D.Create(cw, ch, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        using var e0 = DynamicTexture2D.Create(cw, ch, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var e1 = DynamicTexture2D.Create(cw, ch, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        ClearR32f(h0, 0f);
        ClearR32f(h1, 0f);
        ClearR32f(e0, 0f);
        ClearR32f(e1, 0f);

        // Baseline residual for h=0 is b.
        float rms0 = Rms(rhs.Data);

        // Pre-smooth (Jacobi on fine)
        DynamicTexture2D hFine = RunJacobiIterationsFrom(progJacobi, bFine, start: h0, scratch: h1, iterations: 8);

        // Residual r = b - A*h
        ComputeResidual(progResidual, bFine, hFine, resFine);
        float rms1 = Rms(resFine.ReadPixels());

        // Restrict residual to coarse rhs.
        RenderTo(bCoarse, progRestrict, () =>
        {
            resFine.Bind(0);
            GL.Uniform1(Uniform(progRestrict, "u_fine"), 0);
            GL.Uniform2(Uniform(progRestrict, "u_fineSize"), w, h);
            GL.Uniform2(Uniform(progRestrict, "u_coarseSize"), cw, ch);
        });

        // Solve coarse error approximately: A*e = bCoarse
        DynamicTexture2D eCoarse = RunJacobiIterationsFrom(progJacobi, bCoarse, start: e0, scratch: e1, iterations: 40);

        // Prolongate + add correction: h = h + P(e)
        // Prolongate into the other fine texture.
        DynamicTexture2D hCorrected = ReferenceEquals(hFine, h0) ? h1 : h0;
        RenderTo(hCorrected, progProlongate, () =>
        {
            hFine.Bind(0);
            eCoarse.Bind(1);
            GL.Uniform1(Uniform(progProlongate, "u_fineH"), 0);
            GL.Uniform1(Uniform(progProlongate, "u_coarseE"), 1);
            GL.Uniform2(Uniform(progProlongate, "u_fineSize"), w, h);
            GL.Uniform2(Uniform(progProlongate, "u_coarseSize"), cw, ch);
        });

        // Post-smooth.
        DynamicTexture2D hPost = RunJacobiIterationsFrom(
            progJacobi,
            bFine,
            start: hCorrected,
            scratch: ReferenceEquals(hCorrected, h0) ? h1 : h0,
            iterations: 8);

        // Final residual
        ComputeResidual(progResidual, bFine, hPost, resFine);
        float rms2 = Rms(resFine.ReadPixels());

        Assert.True(IsFinite(rms0) && IsFinite(rms1) && IsFinite(rms2), $"Non-finite residuals for '{name}'");
        Assert.True(rms0 > 1e-6f, $"RHS too small for '{name}'");

        // Desired behavior: a single V-cycle should reduce residual substantially for smooth-ish inputs.
        Assert.True(rms2 < rms1 * 0.85f, $"V-cycle did not reduce residual enough for '{name}': rms1={rms1:0.0000}, rms2={rms2:0.0000}");
        Assert.True(rms2 < rms0 * 0.75f, $"V-cycle did not improve enough over baseline for '{name}': rms0={rms0:0.0000}, rms2={rms2:0.0000}");
    }

    [Theory]
    [MemberData(nameof(VCycleCases))]
    public void MultigridVCycle_ThreeLevel_ReducesResidualMore(string name, RhsCase rhs)
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        using var helper = CreateHelperOrSkip();

        int progJacobi = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_jacobi.fsh");
        int progResidual = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_residual.fsh");
        int progRestrict = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_restrict.fsh");
        int progProlongate = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_prolongate_add.fsh");

        int w = rhs.Width;
        int h = rhs.Height;
        int mw = Math.Max(1, w / 2);
        int mh = Math.Max(1, h / 2);
        int cw = Math.Max(1, mw / 2);
        int ch = Math.Max(1, mh / 2);

        using var bFine = DynamicTexture2D.CreateWithData(w, h, PixelInternalFormat.R32f, rhs.Data, TextureFilterMode.Nearest);

        // Fine solution buffers.
        using var h0 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var h1 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var resFine = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        // Mid (coarse-1) buffers.
        using var bMid = DynamicTexture2D.Create(mw, mh, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var eMid0 = DynamicTexture2D.Create(mw, mh, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var eMid1 = DynamicTexture2D.Create(mw, mh, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var resMid = DynamicTexture2D.Create(mw, mh, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        // Coarsest buffers.
        using var bCoarse = DynamicTexture2D.Create(cw, ch, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var eCoarse0 = DynamicTexture2D.Create(cw, ch, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
        using var eCoarse1 = DynamicTexture2D.Create(cw, ch, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

        ClearR32f(h0, 0f);
        ClearR32f(h1, 0f);
        ClearR32f(eMid0, 0f);
        ClearR32f(eMid1, 0f);
        ClearR32f(eCoarse0, 0f);
        ClearR32f(eCoarse1, 0f);

        // Baseline residual for h=0 is b.
        float rms0 = Rms(rhs.Data);

        // Pre-smooth on fine.
        DynamicTexture2D hFine = RunJacobiIterationsFrom(progJacobi, bFine, start: h0, scratch: h1, iterations: 8);

        // Residual on fine.
        ComputeResidual(progResidual, bFine, hFine, resFine);
        float rmsPre = Rms(resFine.ReadPixels());

        // Compute a 2-level V-cycle result for comparison.
        float rms2LevelFinal;
        {
            using var bCoarse2 = DynamicTexture2D.Create(mw, mh, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var eC0 = DynamicTexture2D.Create(mw, mh, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var eC1 = DynamicTexture2D.Create(mw, mh, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            ClearR32f(eC0, 0f);
            ClearR32f(eC1, 0f);

            // Restrict fine residual -> coarse rhs.
            RenderTo(bCoarse2, progRestrict, () =>
            {
                resFine.Bind(0);
                GL.Uniform1(Uniform(progRestrict, "u_fine"), 0);
                GL.Uniform2(Uniform(progRestrict, "u_fineSize"), w, h);
                GL.Uniform2(Uniform(progRestrict, "u_coarseSize"), mw, mh);
            });

            // Coarse solve.
            DynamicTexture2D eCoarse2 = RunJacobiIterationsFrom(progJacobi, bCoarse2, start: eC0, scratch: eC1, iterations: 60);

            // Prolongate into the opposite fine buffer.
            DynamicTexture2D hFineCorrected2 = ReferenceEquals(hFine, h0) ? h1 : h0;
            RenderTo(hFineCorrected2, progProlongate, () =>
            {
                hFine.Bind(0);
                eCoarse2.Bind(1);
                GL.Uniform1(Uniform(progProlongate, "u_fineH"), 0);
                GL.Uniform1(Uniform(progProlongate, "u_coarseE"), 1);
                GL.Uniform2(Uniform(progProlongate, "u_fineSize"), w, h);
                GL.Uniform2(Uniform(progProlongate, "u_coarseSize"), mw, mh);
            });

            // Post-smooth fine.
            DynamicTexture2D hPost2 = RunJacobiIterationsFrom(
                progJacobi,
                bFine,
                start: hFineCorrected2,
                scratch: ReferenceEquals(hFineCorrected2, h0) ? h1 : h0,
                iterations: 8);

            // Residual.
            ComputeResidual(progResidual, bFine, hPost2, resFine);
            rms2LevelFinal = Rms(resFine.ReadPixels());
        }

        // Restrict residual -> mid rhs.
        RenderTo(bMid, progRestrict, () =>
        {
            resFine.Bind(0);
            GL.Uniform1(Uniform(progRestrict, "u_fine"), 0);
            GL.Uniform2(Uniform(progRestrict, "u_fineSize"), w, h);
            GL.Uniform2(Uniform(progRestrict, "u_coarseSize"), mw, mh);
        });

        // --- Mid-level solve (one V-cycle inside) ---
        // Pre-smooth on mid.
        DynamicTexture2D eMid = RunJacobiIterationsFrom(progJacobi, bMid, start: eMid0, scratch: eMid1, iterations: 6);

        // Residual on mid.
        ComputeResidual(progResidual, bMid, eMid, resMid);

        // Restrict residual -> coarse rhs.
        RenderTo(bCoarse, progRestrict, () =>
        {
            resMid.Bind(0);
            GL.Uniform1(Uniform(progRestrict, "u_fine"), 0);
            GL.Uniform2(Uniform(progRestrict, "u_fineSize"), mw, mh);
            GL.Uniform2(Uniform(progRestrict, "u_coarseSize"), cw, ch);
        });

        // Coarse solve (more iterations).
        DynamicTexture2D eCoarse = RunJacobiIterationsFrom(progJacobi, bCoarse, start: eCoarse0, scratch: eCoarse1, iterations: 80);

        // Prolongate coarse correction into mid.
        DynamicTexture2D eMidCorrected = ReferenceEquals(eMid, eMid0) ? eMid1 : eMid0;
        RenderTo(eMidCorrected, progProlongate, () =>
        {
            eMid.Bind(0);
            eCoarse.Bind(1);
            GL.Uniform1(Uniform(progProlongate, "u_fineH"), 0);
            GL.Uniform1(Uniform(progProlongate, "u_coarseE"), 1);
            GL.Uniform2(Uniform(progProlongate, "u_fineSize"), mw, mh);
            GL.Uniform2(Uniform(progProlongate, "u_coarseSize"), cw, ch);
        });

        // Post-smooth mid.
        DynamicTexture2D eMidPost = RunJacobiIterationsFrom(
            progJacobi,
            bMid,
            start: eMidCorrected,
            scratch: ReferenceEquals(eMidCorrected, eMid0) ? eMid1 : eMid0,
            iterations: 6);

        // --- Back to fine: prolongate mid correction into fine ---
        DynamicTexture2D hFineCorrected = ReferenceEquals(hFine, h0) ? h1 : h0;
        RenderTo(hFineCorrected, progProlongate, () =>
        {
            hFine.Bind(0);
            eMidPost.Bind(1);
            GL.Uniform1(Uniform(progProlongate, "u_fineH"), 0);
            GL.Uniform1(Uniform(progProlongate, "u_coarseE"), 1);
            GL.Uniform2(Uniform(progProlongate, "u_fineSize"), w, h);
            GL.Uniform2(Uniform(progProlongate, "u_coarseSize"), mw, mh);
        });

        // Post-smooth fine.
        DynamicTexture2D hPost = RunJacobiIterationsFrom(
            progJacobi,
            bFine,
            start: hFineCorrected,
            scratch: ReferenceEquals(hFineCorrected, h0) ? h1 : h0,
            iterations: 8);

        // Final residual.
        ComputeResidual(progResidual, bFine, hPost, resFine);
        float rmsFinal = Rms(resFine.ReadPixels());

        Assert.True(IsFinite(rms0) && IsFinite(rmsPre) && IsFinite(rmsFinal), $"Non-finite residuals for '{name}'");
        Assert.True(rms0 > 1e-6f, $"RHS too small for '{name}'");

        // Desired behavior:
        // - A 3-level V-cycle should reduce residual meaningfully vs pre-smooth.
        // - It should also improve (or at least not regress) compared to a 2-level V-cycle.
        Assert.True(rmsFinal < rmsPre * 0.85f, $"3-level V-cycle did not reduce residual enough for '{name}': rmsPre={rmsPre:0.0000}, rmsFinal={rmsFinal:0.0000}");
        Assert.True(rmsFinal < rms0 * 0.75f, $"3-level V-cycle did not improve enough over baseline for '{name}': rms0={rms0:0.0000}, rmsFinal={rmsFinal:0.0000}");
        Assert.True(rmsFinal <= rms2LevelFinal * 0.99f, $"3-level V-cycle was not better than 2-level for '{name}': rms2={rms2LevelFinal:0.0000}, rms3={rmsFinal:0.0000}");
    }

    private ShaderTestHelper CreateHelperOrSkip()
    {
        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.SkipWhen(true, "Test shader assets not found in output directory.");
        }

        return new ShaderTestHelper(shaderPath, includePath);
    }

    private static int Compile(ShaderTestHelper helper, string vsh, string fsh)
    {
        var res = helper.CompileAndLink(vsh, fsh);
        Assert.True(res.IsSuccess, res.ErrorMessage);
        return res.ProgramId;
    }

    private static int Uniform(int programId, string name)
    {
        int loc = GL.GetUniformLocation(programId, name);
        Assert.True(loc >= 0, $"Missing uniform '{name}'");
        return loc;
    }

    private void RenderTo(DynamicTexture2D dst, int programId, Action setup)
    {
        using var fbo = GpuFramebuffer.CreateSingle(dst, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create FBO");

        int vao = GL.GenVertexArray();
        GL.BindVertexArray(vao);

        fbo.Bind();
        GL.Viewport(0, 0, dst.Width, dst.Height);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);

        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GL.UseProgram(programId);
        setup();
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.UseProgram(0);
        GpuFramebuffer.Unbind();

        GL.BindVertexArray(0);
        GL.DeleteVertexArray(vao);
    }

    private void ClearR32f(DynamicTexture2D tex, float value)
    {
        using var fbo = GpuFramebuffer.CreateSingle(tex, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create FBO");
        fbo.Bind();
        GL.Viewport(0, 0, tex.Width, tex.Height);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.ClearColor(value, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GpuFramebuffer.Unbind();
    }

    private DynamicTexture2D RunJacobiIterationsFrom(int progJacobi, DynamicTexture2D b, DynamicTexture2D start, DynamicTexture2D scratch, int iterations)
    {
        DynamicTexture2D src = start;
        DynamicTexture2D dst = scratch;

        for (int i = 0; i < iterations; i++)
        {
            RenderTo(dst, progJacobi, () =>
            {
                src.Bind(0);
                b.Bind(1);
                GL.Uniform1(Uniform(progJacobi, "u_h"), 0);
                GL.Uniform1(Uniform(progJacobi, "u_b"), 1);
                GL.Uniform2(Uniform(progJacobi, "u_size"), dst.Width, dst.Height);
            });

            (src, dst) = (dst, src);
        }

        return src;
    }

    private void ComputeResidual(int progResidual, DynamicTexture2D b, DynamicTexture2D h, DynamicTexture2D outResidual)
    {
        RenderTo(outResidual, progResidual, () =>
        {
            h.Bind(0);
            b.Bind(1);
            GL.Uniform1(Uniform(progResidual, "u_h"), 0);
            GL.Uniform1(Uniform(progResidual, "u_b"), 1);
            GL.Uniform2(Uniform(progResidual, "u_size"), outResidual.Width, outResidual.Height);
        });
    }

    private static float[] BuildDeterministicNoise(int width, int height, int seed)
    {
        var rng = new Random(seed);
        float[] data = new float[width * height];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }
        return data;
    }

    private static float[] ComputeResidualCpu(float[] h, float[] b, int width, int height)
    {
        float[] r = new float[width * height];

        for (int y = 0; y < height; y++)
        {
            int yUp = (y + 1) % height;
            int yDn = (y - 1 + height) % height;

            for (int x = 0; x < width; x++)
            {
                int xRt = (x + 1) % width;
                int xLt = (x - 1 + width) % width;

                float hC = h[y * width + x];
                float hL = h[y * width + xLt];
                float hR = h[y * width + xRt];
                float hD = h[yDn * width + x];
                float hU = h[yUp * width + x];

                float Ah = (hL + hR + hD + hU) - 4f * hC;
                r[y * width + x] = b[y * width + x] - Ah;
            }
        }

        return r;
    }

    private static float[] ComputeRestrictCpu(float[] fine, int fineW, int fineH, int coarseW, int coarseH)
    {
        float[] coarse = new float[coarseW * coarseH];

        for (int cy = 0; cy < coarseH; cy++)
        {
            for (int cx = 0; cx < coarseW; cx++)
            {
                int fx0 = (cx * 2) % fineW;
                int fy0 = (cy * 2) % fineH;

                float r00 = fine[fy0 * fineW + fx0];
                float r10 = fine[fy0 * fineW + ((fx0 + 1) % fineW)];
                float r01 = fine[((fy0 + 1) % fineH) * fineW + fx0];
                float r11 = fine[((fy0 + 1) % fineH) * fineW + ((fx0 + 1) % fineW)];

                coarse[cy * coarseW + cx] = 0.25f * (r00 + r10 + r01 + r11);
            }
        }

        return coarse;
    }

    private static float[] ComputeProlongateAddCpu(float[] fineH, float[] coarseE, int fineW, int fineHh, int coarseW, int coarseH)
    {
        float[] outH = new float[fineW * fineHh];

        for (int y = 0; y < fineHh; y++)
        {
            for (int x = 0; x < fineW; x++)
            {
                int c0x = (x / 2) % coarseW;
                int c0y = (y / 2) % coarseH;

                int c1x = (c0x + 1) % coarseW;
                int c1y = (c0y + 1) % coarseH;

                float fx = (x & 1) * 0.5f;
                float fy = (y & 1) * 0.5f;

                float e00 = coarseE[c0y * coarseW + c0x];
                float e10 = coarseE[c0y * coarseW + c1x];
                float e01 = coarseE[c1y * coarseW + c0x];
                float e11 = coarseE[c1y * coarseW + c1x];

                float e0 = Lerp(e00, e10, fx);
                float e1 = Lerp(e01, e11, fx);
                float e = Lerp(e0, e1, fy);

                outH[y * fineW + x] = fineH[y * fineW + x] + e;
            }
        }

        return outH;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Rms(float[] v)
    {
        double sum = 0;
        for (int i = 0; i < v.Length; i++)
        {
            double x = v[i];
            sum += x * x;
        }
        return (float)Math.Sqrt(sum / Math.Max(1, v.Length));
    }

    private static bool IsFinite(float v) => !(float.IsNaN(v) || float.IsInfinity(v));

    public static RhsCase BuildZeroMeanRhs(int width, int height, RhsKind kind)
    {
        float[] data = new float[width * height];

        switch (kind)
        {
            case RhsKind.SineMix:
                for (int y = 0; y < height; y++)
                {
                    float fy = (float)y / height;
                    for (int x = 0; x < width; x++)
                    {
                        float fx = (float)x / width;
                        data[y * width + x] = (float)(Math.Sin(2.0 * Math.PI * fx) + 0.5 * Math.Cos(2.0 * Math.PI * fy));
                    }
                }
                break;

            case RhsKind.DeterministicNoise:
                data = BuildDeterministicNoise(width, height, seed: 12345);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown RHS kind");
        }

        float mean = 0f;
        for (int i = 0; i < data.Length; i++) mean += data[i];
        mean /= Math.Max(1, data.Length);
        for (int i = 0; i < data.Length; i++) data[i] -= mean;

        return new RhsCase(width, height, data);
    }

    public enum RhsKind
    {
        SineMix,
        DeterministicNoise,
    }

    public readonly record struct RhsCase(int Width, int Height, float[] Data);
}
