using VanillaGraphicsExpanded.Tests.Component.PBR.Materials.Fixtures;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR.Materials;

using Xunit;



namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;
/// <summary>Checks the shipped residual, restriction, prolongation, and multigrid bake passes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrPoissonMultigridVCycleTests : RenderTestBase
{
    #region Public API
    /// <summary>Creates this integration suite over the shared headless OpenGL fixture.</summary>
    public PbrPoissonMultigridVCycleTests(HeadlessGLFixture fixture) : base(fixture)
    {
        this.fixture = fixture;
    }

    #region Shader equivalence tests
    /// <summary>Compares the production residual image with an independent periodic CPU stencil.</summary>
    [Fact]
    public void ResidualShader_MatchesCpuStencil_OnRandomInputs()
    {
        EnsureContextValid();
        fixture.MakeCurrent();
        // Arrange: preserve the two seeded noise images and their 8x8 periodic domain.
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();

        using var programs = new PbrHeightBakeTestPrograms();
        var progResidual = programs.Create("pbr_heightbake_residual");

        const int w = 8;
        const int h = 8;

        float[] hData = BuildDeterministicNoise(w, h, seed: 101);
        float[] bData = BuildDeterministicNoise(w, h, seed: 202);

        using var hTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((hData).AsSpan()).ToArray())]).RequireResource();
        using var bTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((bData).AsSpan()).ToArray())]).RequireResource();
        using var rTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        // Act: submit the shipped residual pass over the exact authored input images.
        RenderTo(programs, rTex, progResidual, program =>
        {
            progResidual.h = hTex;
            progResidual.b = bTex;
            programs.Parameters.CommonSize = (w, h);
        });

        // Assert: compare every GPU result against the independent wrapped stencil oracle.
        float[] cpu = ComputeResidualCpu(hData, bData, w, h);
        ImageAssertions.Comparisons.Compare(rTex, cpu, 1e-4f).AssertEqual();
    }

    /// <summary>Compares the production restriction image with the authored four-sample box filter.</summary>
    [Fact]
    public void RestrictShader_MatchesCpuBoxFilter()
    {
        EnsureContextValid();
        fixture.MakeCurrent();
        // Arrange: retain the seeded 8x8 fine image and 4x4 output extent.
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();

        using var programs = new PbrHeightBakeTestPrograms();
        var progRestrict = programs.Create("pbr_heightbake_restrict");

        const int fineW = 8;
        const int fineH = 8;
        const int coarseW = 4;
        const int coarseH = 4;

        float[] fine = BuildDeterministicNoise(fineW, fineH, seed: 303);

        using var fineTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = fineW, Height = fineH, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, fineW, fineH), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((fine).AsSpan()).ToArray())]).RequireResource();
        using var coarseTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, coarseW), Height = System.Math.Max(1, coarseH), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        // Act: execute the shipped four-sample restriction pass.
        RenderTo(programs, coarseTex, progRestrict, program =>
        {
            progRestrict.fine = fineTex;
            programs.Parameters.MultigridFineSize = (fineW, fineH);
            programs.Parameters.MultigridCoarseSize = (coarseW, coarseH);
            programs.Parameters.CommonSize = (0, 0);
        });

        // Assert: compare all coarse texels against the independently calculated box filter.
        float[] cpu = ComputeRestrictCpu(fine, fineW, fineH, coarseW, coarseH);
        ImageAssertions.Comparisons.Compare(coarseTex, cpu, 1e-4f).AssertEqual();
    }

    /// <summary>Compares production prolongation and addition with an independent bilinear CPU oracle.</summary>
    [Fact]
    public void ProlongateAddShader_MatchesCpuBilinearPlusH()
    {
        EnsureContextValid();
        fixture.MakeCurrent();
        // Arrange: retain the distinct seeded fine/coarse inputs and their original extents.
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();

        using var programs = new PbrHeightBakeTestPrograms();
        var progProlongate = programs.Create("pbr_heightbake_prolongate_add");

        const int fineW = 8;
        const int fineH = 8;
        const int coarseW = 4;
        const int coarseH = 4;

        float[] fineHData = BuildDeterministicNoise(fineW, fineH, seed: 404);
        float[] coarseE = BuildDeterministicNoise(coarseW, coarseH, seed: 505);

        using var fineTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = fineW, Height = fineH, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, fineW, fineH), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((fineHData).AsSpan()).ToArray())]).RequireResource();
        using var coarseTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = coarseW, Height = coarseH, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, coarseW, coarseH), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((coarseE).AsSpan()).ToArray())]).RequireResource();
        using var outTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, fineW), Height = System.Math.Max(1, fineH), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        // Act: execute the shipped prolongation-plus-addition pass.
        RenderTo(programs, outTex, progProlongate, program =>
        {
            progProlongate.fineH = fineTex;
            progProlongate.coarseE = coarseTex;
            programs.Parameters.MultigridFineSize = (fineW, fineH);
            programs.Parameters.MultigridCoarseSize = (coarseW, coarseH);
            programs.Parameters.CommonSize = (0, 0);
        });

        // Assert: compare the complete fine result with the independent bilinear oracle.
        float[] cpu = ComputeProlongateAddCpu(fineHData, coarseE, fineW, fineH, coarseW, coarseH);
        ImageAssertions.Comparisons.Compare(outTex, cpu, 1e-4f).AssertEqual();
    }

    #endregion

    #region V-cycle tests and input data
    /// <summary>Provides the original sine and deterministic-noise inputs for V-cycle coverage.</summary>
    public static IEnumerable<object[]> VCycleCases()
    {
        yield return new object[] { "sine", BuildZeroMeanRhs(32, 32, RhsKind.SineMix) };
        yield return new object[] { "noise", BuildZeroMeanRhs(32, 32, RhsKind.DeterministicNoise) };
    }

    [MemberData(nameof(VCycleCases))]
    /// <summary>Verifies one two-level V-cycle reduces the same baseline and pre-smooth residuals.</summary>
    [Theory]
    public void MultigridVCycle_ReducesResidual(string name, RhsCase rhs)
    {
        EnsureContextValid();
        fixture.MakeCurrent();
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();

        // Arrange: preserve the selected RHS, grid hierarchy and authored two-level solver workload.
        using var programs = new PbrHeightBakeTestPrograms();
        var progJacobi = programs.Create("pbr_heightbake_jacobi");
        var progResidual = programs.Create("pbr_heightbake_residual");
        var progRestrict = programs.Create("pbr_heightbake_restrict");
        var progProlongate = programs.Create("pbr_heightbake_prolongate_add");

        int w = rhs.Width;
        int h = rhs.Height;
        int cw = Math.Max(1, w / 2);
        int ch = Math.Max(1, h / 2);

        using var bFine = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((rhs.Data).AsSpan()).ToArray())]).RequireResource();

        using var h0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var h1 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        using var resFine = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var bCoarse = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, cw), Height = System.Math.Max(1, ch), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        using var e0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, cw), Height = System.Math.Max(1, ch), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var e1 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, cw), Height = System.Math.Max(1, ch), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        ClearR32f(h0, 0f);
        ClearR32f(h1, 0f);
        ClearR32f(e0, 0f);
        ClearR32f(e1, 0f);

        // Baseline residual for h=0 is b.
        // Act: run the authored pre-smooth, restriction, coarse solve, prolongation and post-smooth steps.
        float rms0 = Rms(rhs.Data);

        // Pre-smooth (Jacobi on fine)
        DynamicTexture2D hFine = RunJacobiIterationsFrom(programs, progJacobi, bFine, start: h0, scratch: h1, iterations: 8);

        // Residual r = b - A*h
        ComputeResidual(programs, progResidual, bFine, hFine, resFine);
        float rms1 = Rms(resFine);

        // Restrict residual to coarse rhs.
        RenderTo(programs, bCoarse, progRestrict, (program) => {
            program.fine = resFine;
            programs.Parameters.MultigridFineSize = (w, h);
            programs.Parameters.MultigridCoarseSize = (cw, ch);
            programs.Parameters.CommonSize = (0, 0);
        });

        // Solve coarse error approximately: A*e = bCoarse
        DynamicTexture2D eCoarse = RunJacobiIterationsFrom(programs, progJacobi, bCoarse, start: e0, scratch: e1, iterations: 40);

        // Prolongate + add correction: h = h + P(e)
        // Prolongate into the other fine texture.
        DynamicTexture2D hCorrected = ReferenceEquals(hFine, h0) ? h1 : h0;
        RenderTo(programs, hCorrected, progProlongate, (program) => {
            program.fineH = hFine;
            program.coarseE = eCoarse;
            programs.Parameters.MultigridFineSize = (w, h);
            programs.Parameters.MultigridCoarseSize = (cw, ch);
            programs.Parameters.CommonSize = (0, 0);
        });

        // Post-smooth.
        DynamicTexture2D hPost = RunJacobiIterationsFrom(
            programs,
            progJacobi,
            bFine,
            start: hCorrected,
            scratch: ReferenceEquals(hCorrected, h0) ? h1 : h0,
            iterations: 8);

        // Final residual
        ComputeResidual(programs, progResidual, bFine, hPost, resFine);
        float rms2 = Rms(resFine);

        // Assert: retain all finite, baseline, and pre-smooth residual guarantees.
        Assert.True(IsFinite(rms0) && IsFinite(rms1) && IsFinite(rms2), $"Non-finite residuals for '{name}'");
        Assert.True(rms0 > 1e-6f, $"RHS too small for '{name}'");

        // Desired behavior: a single V-cycle should reduce residual substantially for smooth-ish inputs.
        Assert.True(rms2 < rms1 * 0.85f, $"V-cycle did not reduce residual enough for '{name}': rms1={rms1:0.0000}, rms2={rms2:0.0000}");
        Assert.True(rms2 < rms0 * 0.75f, $"V-cycle did not improve enough over baseline for '{name}': rms0={rms0:0.0000}, rms2={rms2:0.0000}");
    }

    [MemberData(nameof(VCycleCases))]
    /// <summary>Retains the existing two-level and three-level residual assertions for each authored input.</summary>
    [Theory]
    public void MultigridVCycle_ThreeLevel_ReducesResidualMore(string name, RhsCase rhs)
    {
        EnsureContextValid();
        fixture.MakeCurrent();
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();

        // Arrange: preserve the original three-grid hierarchy and two-level comparison workload.
        var progJacobi = programs.Create("pbr_heightbake_jacobi");
        var progResidual = programs.Create("pbr_heightbake_residual");
        var progRestrict = programs.Create("pbr_heightbake_restrict");
        var progProlongate = programs.Create("pbr_heightbake_prolongate_add");

        int w = rhs.Width;
        int h = rhs.Height;
        int mw = Math.Max(1, w / 2);
        int mh = Math.Max(1, h / 2);
        int cw = Math.Max(1, mw / 2);
        int ch = Math.Max(1, mh / 2);
        using var bFine = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((rhs.Data).AsSpan()).ToArray())]).RequireResource();
        using var h0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var h1 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var resFine = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();


        // Fine solution buffers.

        // Mid (coarse-1) buffers.
        using var bMid = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, mw), Height = System.Math.Max(1, mh), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var eMid0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, mw), Height = System.Math.Max(1, mh), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var eMid1 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, mw), Height = System.Math.Max(1, mh), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var resMid = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, mw), Height = System.Math.Max(1, mh), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var bCoarse = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, cw), Height = System.Math.Max(1, ch), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        // Coarsest buffers.
        using var eCoarse0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, cw), Height = System.Math.Max(1, ch), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var eCoarse1 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, cw), Height = System.Math.Max(1, ch), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        ClearR32f(h0, 0f);
        ClearR32f(h1, 0f);
        ClearR32f(eMid0, 0f);
        ClearR32f(eMid1, 0f);
        ClearR32f(eCoarse0, 0f);
        ClearR32f(eCoarse1, 0f);

        // Act: run pre-smoothing, the two-level comparator, and the complete three-level V-cycle.
        // Baseline residual for h=0 is b.
        float rms0 = Rms(rhs.Data);

        // Pre-smooth on fine.
        DynamicTexture2D hFine = RunJacobiIterationsFrom(programs, progJacobi, bFine, start: h0, scratch: h1, iterations: 8);

        // Residual on fine.
        ComputeResidual(programs, progResidual, bFine, hFine, resFine);
        float rmsPre = Rms(resFine);

        // Compute the two-level V-cycle result using the original shared fine buffers.
        float rms2LevelFinal;
        {
            using var bCoarse2 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, mw), Height = System.Math.Max(1, mh), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var eC0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, mw), Height = System.Math.Max(1, mh), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var eC1 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, mw), Height = System.Math.Max(1, mh), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            ClearR32f(eC0, 0f);
            ClearR32f(eC1, 0f);

            // Restrict the pre-smoothed fine residual to the two-level control grid.
            RenderTo(programs, bCoarse2, progRestrict, (program) => {
                program.fine = resFine;
                programs.Parameters.MultigridFineSize = (w, h);
                programs.Parameters.MultigridCoarseSize = (mw, mh);
                programs.Parameters.CommonSize = (0, 0);
            });

            DynamicTexture2D eCoarse2 = RunJacobiIterationsFrom(programs, progJacobi, bCoarse2, start: eC0, scratch: eC1, iterations: 60);

            DynamicTexture2D hFineCorrected2 = ReferenceEquals(hFine, h0) ? h1 : h0;
            RenderTo(programs, hFineCorrected2, progProlongate, (program) => {
                program.fineH = hFine;
                program.coarseE = eCoarse2;
                programs.Parameters.MultigridFineSize = (w, h);
                programs.Parameters.MultigridCoarseSize = (mw, mh);
                programs.Parameters.CommonSize = (0, 0);
            });

            DynamicTexture2D hPost2 = RunJacobiIterationsFrom(
                programs,
                progJacobi,
                bFine,
                start: hFineCorrected2,
                scratch: ReferenceEquals(hFineCorrected2, h0) ? h1 : h0,
                iterations: 8);

            ComputeResidual(programs, progResidual, bFine, hPost2, resFine);
            rms2LevelFinal = Rms(resFine);
        }

        // Restrict residual -> mid rhs.
        RenderTo(programs, bMid, progRestrict, (program) => {
            program.fine = resFine;
            programs.Parameters.MultigridFineSize = (w, h);
            programs.Parameters.MultigridCoarseSize = (mw, mh);
            programs.Parameters.CommonSize = (0, 0);
        });

        // --- Mid-level solve (one V-cycle inside) ---
        // Pre-smooth on mid.
        DynamicTexture2D eMid = RunJacobiIterationsFrom(programs, progJacobi, bMid, start: eMid0, scratch: eMid1, iterations: 6);

        // Residual on mid.
        ComputeResidual(programs, progResidual, bMid, eMid, resMid);

        // Restrict residual -> coarse rhs.
        RenderTo(programs, bCoarse, progRestrict, (program) => {
            program.fine = resMid;
            programs.Parameters.MultigridFineSize = (mw, mh);
            programs.Parameters.MultigridCoarseSize = (cw, ch);
            programs.Parameters.CommonSize = (0, 0);
        });

        // Coarse solve (more iterations).
        DynamicTexture2D eCoarse = RunJacobiIterationsFrom(programs, progJacobi, bCoarse, start: eCoarse0, scratch: eCoarse1, iterations: 80);

        // Prolongate coarse correction into mid.
        DynamicTexture2D eMidCorrected = ReferenceEquals(eMid, eMid0) ? eMid1 : eMid0;
        RenderTo(programs, eMidCorrected, progProlongate, (program) => {
            program.fineH = eMid;
            program.coarseE = eCoarse;
            programs.Parameters.MultigridFineSize = (mw, mh);
            programs.Parameters.MultigridCoarseSize = (cw, ch);
            programs.Parameters.CommonSize = (0, 0);
        });

        // Post-smooth mid.
        DynamicTexture2D eMidPost = RunJacobiIterationsFrom(
            programs,
            progJacobi,
            bMid,
            start: eMidCorrected,
            scratch: ReferenceEquals(eMidCorrected, eMid0) ? eMid1 : eMid0,
            iterations: 6);

        // --- Back to fine: prolongate mid correction into fine ---
        DynamicTexture2D hFineCorrected = ReferenceEquals(hFine, h0) ? h1 : h0;
        RenderTo(programs, hFineCorrected, progProlongate, (program) => {
            program.fineH = hFine;
            program.coarseE = eMidPost;
            programs.Parameters.MultigridFineSize = (w, h);
            programs.Parameters.MultigridCoarseSize = (mw, mh);
            programs.Parameters.CommonSize = (0, 0);
        });

        // Post-smooth fine.
        DynamicTexture2D hPost = RunJacobiIterationsFrom(
            programs,
            progJacobi,
            bFine,
            start: hFineCorrected,
            scratch: ReferenceEquals(hFineCorrected, h0) ? h1 : h0,
            iterations: 8);

        // Final residual.
        ComputeResidual(programs, progResidual, bFine, hPost, resFine);
        float rmsFinal = Rms(resFine);

        // Assert: preserve finite residuals and the original two-level/three-level thresholds.
        Assert.True(IsFinite(rms0) && IsFinite(rmsPre) && IsFinite(rmsFinal), $"Non-finite residuals for '{name}'");
        Assert.True(rms0 > 1e-6f, $"RHS too small for '{name}'");

        // Desired behavior:
        // - A 3-level V-cycle should reduce residual meaningfully vs pre-smooth.
        // - It should also improve (or at least not regress) compared to a 2-level V-cycle.
        Assert.True(rmsFinal < rmsPre * 0.85f, $"3-level V-cycle did not reduce residual enough for '{name}': rmsPre={rmsPre:0.0000}, rmsFinal={rmsFinal:0.0000}");
        Assert.True(rmsFinal < rms0 * 0.75f, $"3-level V-cycle did not improve enough over baseline for '{name}': rms0={rms0:0.0000}, rmsFinal={rmsFinal:0.0000}");
        Assert.True(rmsFinal <= rms2LevelFinal * 0.99f, $"3-level V-cycle was not better than 2-level for '{name}': rms2={rms2LevelFinal:0.0000}, rms3={rmsFinal:0.0000}");
    }

    /// <summary>Builds a zero-mean right-hand side for the requested deterministic pattern.</summary>
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

    /// <summary>Names the preserved periodic right-hand-side patterns.</summary>
    public enum RhsKind
    {
        SineMix,
        DeterministicNoise,
    }

    /// <summary>Stores dimensions and values for one authored periodic right-hand side.</summary>
    public readonly record struct RhsCase(int Width, int Height, float[] Data);
    #endregion
    #endregion

    #region Private
    private readonly HeadlessGLFixture fixture;

    #region Production pass execution
    /// <summary>Submits a production bake pass after assigning its declared shader inputs.</summary>
    private void RenderTo(PbrHeightBakeTestPrograms programs, DynamicTexture2D dst, PbrHeightBakeShaderProgram program, Action<PbrHeightBakeShaderProgram> setup)
    {
        setup(program);
        programs.Draw(dst, program, clear: true);
    }

    /// <summary>Initializes one scalar texture using the RHI clear operation.</summary>
    private void ClearR32f(DynamicTexture2D tex, float value)
    {
        Span<float> clear = stackalloc float[1] { value };
        RHICommandContext.Current.ClearTexture(tex, new(new(), 0, 0, 0, tex.Width, tex.Height),
            new(PixelChannelLayout.Red, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes(clear));
    }

    /// <summary>Runs the authored number of production Jacobi ping-pong iterations.</summary>
    private DynamicTexture2D RunJacobiIterationsFrom(PbrHeightBakeTestPrograms programs, PbrHeightBakeShaderProgram program, DynamicTexture2D b, DynamicTexture2D start, DynamicTexture2D scratch, int iterations)
    {
        DynamicTexture2D src = start;
        DynamicTexture2D dst = scratch;

        for (int i = 0; i < iterations; i++)
        {
            RenderTo(programs, dst, program, (pass) =>
            {
                pass.h = src;
                pass.b = b;
                programs.Parameters.CommonSize = (dst.Width, dst.Height);
            });

            (src, dst) = (dst, src);
        }

        return src;
    }

    /// <summary>Evaluates the production periodic residual shader into the supplied output.</summary>
    private void ComputeResidual(PbrHeightBakeTestPrograms programs, PbrHeightBakeShaderProgram program, DynamicTexture2D b, DynamicTexture2D h, DynamicTexture2D outResidual)
    {
        RenderTo(programs, outResidual, program, (pass) =>
        {
            pass.h = h;
            pass.b = b;
            programs.Parameters.CommonSize = (outResidual.Width, outResidual.Height);
        });
    }

    #endregion

    #region Independent CPU oracles
    /// <summary>Builds the original seeded deterministic noise pattern.</summary>
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

    /// <summary>Computes the independent periodic residual stencil for every texel.</summary>
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

    /// <summary>Computes the independent wrapped four-sample restriction filter.</summary>
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

    /// <summary>Computes the independent wrapped bilinear correction added to the fine height field.</summary>
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

    /// <summary>Interpolates two scalar oracle samples using the production linear formula.</summary>
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    #endregion

    #region Residual metrics and observation setup
    /// <summary>Computes the same double-squared RMS over every residual texel with a compact GPU reduction.</summary>
    private float Rms(GpuTexture image)
    {
        var result = ImageAssertions.Statistics.Statistics(image);
        result.AssertFiniteRange();
        return (float)Math.Sqrt(result.SumSquares(0) / (image.Width * image.Height));
    }

    /// <summary>Computes the independent double-accumulated CPU RMS.</summary>
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

    /// <summary>Reports whether one scalar is neither NaN nor infinite.</summary>
    private static bool IsFinite(float v) => !(float.IsNaN(v) || float.IsInfinity(v));

    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }
    #endregion
    #endregion
}
