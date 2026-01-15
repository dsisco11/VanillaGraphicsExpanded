using System;
using System.Collections.Generic;
using System.Linq;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// GPU-side bake for the VGE normal+height sidecar atlas.
///
/// Implementation: per-texture (per atlas-rect) pipeline:
/// - derive band-passed luminance detail
/// - build desired slope field
/// - solve periodic Poisson (multigrid V-cycle)
/// - normalize + optional gamma shaping
/// - write packed normal (RGB, 0..1) + signed height (A) into atlas sidecar
///
/// This runs during loading / atlas build time.
/// </summary>
internal static class PbrNormalDepthAtlasGpuBaker
{
    // Asset domain for shader source.
    private const string Domain = "vanillagraphicsexpanded";

    // Shader asset names (without extension).
    private const string Vsh = "pbr_heightbake_fullscreen";
    private const string FshLuminance = "pbr_heightbake_luminance";
    private const string FshGauss1D = "pbr_heightbake_gauss1d";
    private const string FshSub = "pbr_heightbake_sub";
    private const string FshCombine = "pbr_heightbake_combine";
    private const string FshGradient = "pbr_heightbake_gradient";
    private const string FshDivergence = "pbr_heightbake_divergence";
    private const string FshJacobi = "pbr_heightbake_jacobi";
    private const string FshResidual = "pbr_heightbake_residual";
    private const string FshRestrict = "pbr_heightbake_restrict";
    private const string FshProlongateAdd = "pbr_heightbake_prolongate_add";
    private const string FshNormalize = "pbr_heightbake_normalize";
    private const string FshPackToAtlas = "pbr_heightbake_pack_to_atlas";
    private const string FshCopy = "pbr_heightbake_copy";

    private const int MaxRadius = 64;

    private static bool initialized;
    private static int vao;
    private static GBuffer? scratchFbo;

    private static VgeStageNamedShaderProgram? progLuminance;
    private static VgeStageNamedShaderProgram? progGauss1D;
    private static VgeStageNamedShaderProgram? progSub;
    private static VgeStageNamedShaderProgram? progCombine;
    private static VgeStageNamedShaderProgram? progGradient;
    private static VgeStageNamedShaderProgram? progDivergence;
    private static VgeStageNamedShaderProgram? progJacobi;
    private static VgeStageNamedShaderProgram? progResidual;
    private static VgeStageNamedShaderProgram? progRestrict;
    private static VgeStageNamedShaderProgram? progProlongateAdd;
    private static VgeStageNamedShaderProgram? progNormalize;
    private static VgeStageNamedShaderProgram? progPackToAtlas;
    private static VgeStageNamedShaderProgram? progCopy;

    // Intermediate textures are reused and resized per tile.
    private static TileResources? tile;
    private static MultigridResources? mg;

    public static void BakePerTexture(
        ICoreClientAPI capi,
        int baseAlbedoAtlasPageTexId,
        int destNormalDepthTexId,
        int atlasWidth,
        int atlasHeight,
        IEnumerable<TextureAtlasPosition> texturePositions)
    {
        ArgumentNullException.ThrowIfNull(capi);
        if (baseAlbedoAtlasPageTexId == 0) throw new ArgumentOutOfRangeException(nameof(baseAlbedoAtlasPageTexId));
        if (destNormalDepthTexId == 0) throw new ArgumentOutOfRangeException(nameof(destNormalDepthTexId));
        if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));
        ArgumentNullException.ThrowIfNull(texturePositions);

        try
        {
            EnsureInitialized(capi);
        }
        catch
        {
            // Best-effort: if shaders fail to compile/load, leave the atlas at defaults.
            return;
        }

        // Filter to just this atlas page.
        var positions = texturePositions.Where(p => p is not null && p.atlasTextureId == baseAlbedoAtlasPageTexId).ToArray();
        if (positions.Length == 0)
        {
            // Nothing to bake for this atlas page.
            return;
        }

        // Save minimal GL state.
        int[] prevViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, prevViewport);
        int prevFbo = GBuffer.SaveBinding();
        GL.GetInteger(GetPName.VertexArrayBinding, out int prevVao);
        GL.GetInteger(GetPName.CurrentProgram, out int prevProgram);
        GL.GetInteger(GetPName.ActiveTexture, out int prevActiveTex);
        GL.GetInteger(GetPName.TextureBinding2D, out int prevTex2D);

        try
        {
            scratchFbo!.Bind();
            GL.BindVertexArray(vao);

            // Clear entire atlas sidecar first (deterministic baseline).
            BindAtlasTarget(destNormalDepthTexId, 0, 0, atlasWidth, atlasHeight);
            GL.ClearColor(0.5f, 0.5f, 1.0f, 0.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            foreach (TextureAtlasPosition pos in positions)
            {
                if (!TryGetRectPx(pos, atlasWidth, atlasHeight, out int rx, out int ry, out int rw, out int rh))
                {
                    continue;
                }

                // Very small tiles aren’t worth baking; leave defaults.
                if (rw < 4 || rh < 4)
                {
                    continue;
                }

                int solverW = rw;
                int solverH = rh;

                tile ??= new TileResources();
                tile.EnsureSize(solverW, solverH);

                mg ??= new MultigridResources();
                mg.EnsureSize(solverW, solverH);

                var cfg = ConfigModSystem.Config;
                var bake = cfg.NormalDepthBake;

                // 1) Luminance (linear) from atlas sub-rect.
                RunLuminancePass(
                    atlasTexId: baseAlbedoAtlasPageTexId,
                    atlasRectPx: (rx, ry, rw, rh),
                    dst: tile.L);

                // 2) Remove low-frequency ramps: base = Gauss(L, sigmaBig), D0 = L - base.
                RunGaussian(tile.L, tile.Tmp, tile.Base, bake.SigmaBig);
                RunSub(tile.L, tile.Base, tile.D0);

                // 3) Multi-scale band-pass on D0.
                RunGaussian(tile.D0, tile.Tmp, tile.G1, bake.Sigma1);
                RunGaussian(tile.D0, tile.Tmp, tile.G2, bake.Sigma2);
                RunGaussian(tile.D0, tile.Tmp, tile.G3, bake.Sigma3);
                RunGaussian(tile.D0, tile.Tmp, tile.G4, bake.Sigma4);
                RunCombine(tile.G1, tile.G2, tile.G3, tile.G4, tile.D, bake.W1, bake.W2, bake.W3);

                // 4) Desired gradient field.
                RunGradient(tile.D, tile.G, bake);

                // 5) Divergence.
                RunDivergence(tile.G, tile.Div);

                // 6) Multigrid Poisson solve: Δh = div (periodic).
                mg.Solve(tile.Div, tile.H, bake);

                // 7) Subtract mean on CPU (fix DC offset) then normalize/gamma on GPU.
                float mean = ComputeMeanR32f(tile.H);
                RunNormalize(tile.H, tile.Hn, mean, bake.HeightStrength, bake.Gamma);

                // 8) Pack to atlas (RGB normal in 0..1, A signed height).
                RunPackToAtlas(
                    dstAtlasTexId: destNormalDepthTexId,
                    viewportOriginPx: (rx, ry),
                    tileSizePx: (rw, rh),
                    solverSizePx: (solverW, solverH),
                    heightTex: tile.Hn,
                    normalStrength: bake.NormalStrength);
            }
        }
        catch
        {
            // Best-effort: on failure, the sidecar remains at default clear values.
        }
        finally
        {
            GL.UseProgram(prevProgram);
            GL.BindVertexArray(prevVao);
                GBuffer.RestoreBinding(prevFbo);
            GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);

            GL.ActiveTexture((TextureUnit)prevActiveTex);
            GL.BindTexture(TextureTarget.Texture2D, prevTex2D);
        }
    }

    private static void EnsureInitialized(ICoreClientAPI capi)
    {
        if (initialized)
        {
            return;
        }

        // Minimal GL objects.
        vao = GL.GenVertexArray();
        scratchFbo = GBuffer.Wrap(GL.GenFramebuffer(), debugName: "vge_bake_scratch");

        // Compile all programs using VGE's shader pipeline (imports, diagnostics, debug labels).
        // Each pass shares the same fullscreen vertex stage, but has its own fragment stage.
        progLuminance = new VgeStageNamedShaderProgram(FshLuminance, Vsh, FshLuminance, Domain);
        progGauss1D = new VgeStageNamedShaderProgram(FshGauss1D, Vsh, FshGauss1D, Domain);
        progSub = new VgeStageNamedShaderProgram(FshSub, Vsh, FshSub, Domain);
        progCombine = new VgeStageNamedShaderProgram(FshCombine, Vsh, FshCombine, Domain);
        progGradient = new VgeStageNamedShaderProgram(FshGradient, Vsh, FshGradient, Domain);
        progDivergence = new VgeStageNamedShaderProgram(FshDivergence, Vsh, FshDivergence, Domain);
        progJacobi = new VgeStageNamedShaderProgram(FshJacobi, Vsh, FshJacobi, Domain);
        progResidual = new VgeStageNamedShaderProgram(FshResidual, Vsh, FshResidual, Domain);
        progRestrict = new VgeStageNamedShaderProgram(FshRestrict, Vsh, FshRestrict, Domain);
        progProlongateAdd = new VgeStageNamedShaderProgram(FshProlongateAdd, Vsh, FshProlongateAdd, Domain);
        progNormalize = new VgeStageNamedShaderProgram(FshNormalize, Vsh, FshNormalize, Domain);
        progPackToAtlas = new VgeStageNamedShaderProgram(FshPackToAtlas, Vsh, FshPackToAtlas, Domain);
        progCopy = new VgeStageNamedShaderProgram(FshCopy, Vsh, FshCopy, Domain);

        progLuminance.Initialize(capi);
        progGauss1D.Initialize(capi);
        progSub.Initialize(capi);
        progCombine.Initialize(capi);
        progGradient.Initialize(capi);
        progDivergence.Initialize(capi);
        progJacobi.Initialize(capi);
        progResidual.Initialize(capi);
        progRestrict.Initialize(capi);
        progProlongateAdd.Initialize(capi);
        progNormalize.Initialize(capi);
        progPackToAtlas.Initialize(capi);
        progCopy.Initialize(capi);

        CompileOrThrow(progLuminance);
        CompileOrThrow(progGauss1D);
        CompileOrThrow(progSub);
        CompileOrThrow(progCombine);
        CompileOrThrow(progGradient);
        CompileOrThrow(progDivergence);
        CompileOrThrow(progJacobi);
        CompileOrThrow(progResidual);
        CompileOrThrow(progRestrict);
        CompileOrThrow(progProlongateAdd);
        CompileOrThrow(progNormalize);
        CompileOrThrow(progPackToAtlas);
        CompileOrThrow(progCopy);

        initialized = true;
    }

    private static void CompileOrThrow(VgeStageNamedShaderProgram program)
    {
        if (!program.CompileAndLink())
        {
            throw new InvalidOperationException($"[VGE] Failed to compile/link bake shader program '{program.PassName}'");
        }
    }

    private static bool TryGetRectPx(TextureAtlasPosition pos, int atlasWidth, int atlasHeight, out int x, out int y, out int w, out int h)
    {
        // Same conversion approach as material param atlas builder.
        int x1 = Clamp((int)Math.Floor(pos.x1 * atlasWidth), 0, atlasWidth - 1);
        int y1 = Clamp((int)Math.Floor(pos.y1 * atlasHeight), 0, atlasHeight - 1);
        int x2 = Clamp((int)Math.Ceiling(pos.x2 * atlasWidth), 0, atlasWidth);
        int y2 = Clamp((int)Math.Ceiling(pos.y2 * atlasHeight), 0, atlasHeight);

        x = x1;
        y = y1;
        w = x2 - x1;
        h = y2 - y1;
        return w > 0 && h > 0;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    private static void DrawFullscreenTriangle()
    {
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private static void BindTarget(DynamicTexture dst)
    {
        scratchFbo!.AttachColor(dst);
        GL.Viewport(0, 0, dst.Width, dst.Height);
    }

    private static void BindAtlasTarget(int atlasTexId, int x, int y, int w, int h)
    {
        scratchFbo!.AttachColorTextureId(atlasTexId);
        GL.Viewport(x, y, w, h);
    }

    private static void RunLuminancePass(int atlasTexId, (int x, int y, int w, int h) atlasRectPx, DynamicTexture dst)
    {
        BindTarget(dst);

        progLuminance!.Use();
        progLuminance.BindTexture2D("u_atlas", atlasTexId, 0);
        progLuminance.Uniform4i("u_atlasRectPx", atlasRectPx.x, atlasRectPx.y, atlasRectPx.w, atlasRectPx.h);
        progLuminance.Uniform2i("u_outSize", dst.Width, dst.Height);

        DrawFullscreenTriangle();
    }

    private static void RunGaussian(DynamicTexture src, DynamicTexture tmp, DynamicTexture dst, float sigma)
    {
        if (sigma <= 0.0001f)
        {
            RunCopy(src, dst);
            return;
        }

        int radius = ComputeRadius(sigma);
        if (radius <= 0)
        {
            RunCopy(src, dst);
            return;
        }

        var weights = BuildGaussianWeights(sigma, radius);

        // Horizontal
        BindTarget(tmp);
        progGauss1D!.Use();
        progGauss1D.BindTexture2D("u_src", src.TextureId, 0);
        progGauss1D.Uniform2i("u_size", src.Width, src.Height);
        progGauss1D.Uniform2i("u_dir", 1, 0);
        progGauss1D.Uniform("u_radius", radius);
        progGauss1D.Uniform1fv("u_weights", weights);
        DrawFullscreenTriangle();

        // Vertical
        BindTarget(dst);
        progGauss1D!.Use();
        progGauss1D.BindTexture2D("u_src", tmp.TextureId, 0);
        progGauss1D.Uniform2i("u_size", dst.Width, dst.Height);
        progGauss1D.Uniform2i("u_dir", 0, 1);
        progGauss1D.Uniform("u_radius", radius);
        progGauss1D.Uniform1fv("u_weights", weights);
        DrawFullscreenTriangle();
    }

    private static void RunSub(DynamicTexture a, DynamicTexture b, DynamicTexture dst)
    {
        BindTarget(dst);
        progSub!.Use();
        progSub.BindTexture2D("u_a", a.TextureId, 0);
        progSub.BindTexture2D("u_b", b.TextureId, 1);
        progSub.Uniform2i("u_size", dst.Width, dst.Height);
        DrawFullscreenTriangle();
    }

    private static void RunCopy(DynamicTexture src, DynamicTexture dst)
    {
        BindTarget(dst);
        progCopy!.Use();
        progCopy.BindTexture2D("u_src", src.TextureId, 0);
        progCopy.Uniform2i("u_size", dst.Width, dst.Height);
        DrawFullscreenTriangle();
    }

    private static void RunCombine(DynamicTexture g1, DynamicTexture g2, DynamicTexture g3, DynamicTexture g4, DynamicTexture dst, float w1, float w2, float w3)
    {
        BindTarget(dst);
        progCombine!.Use();
        progCombine.BindTexture2D("u_g1", g1.TextureId, 0);
        progCombine.BindTexture2D("u_g2", g2.TextureId, 1);
        progCombine.BindTexture2D("u_g3", g3.TextureId, 2);
        progCombine.BindTexture2D("u_g4", g4.TextureId, 3);
        progCombine.Uniform3f("u_w", w1, w2, w3);
        progCombine.Uniform2i("u_size", dst.Width, dst.Height);
        DrawFullscreenTriangle();
    }

    private static void RunGradient(DynamicTexture d, DynamicTexture dstG, LumOnConfig.NormalDepthBakeConfig bake)
    {
        BindTarget(dstG);
        progGradient!.Use();
        progGradient.BindTexture2D("u_d", d.TextureId, 0);
        progGradient.Uniform2i("u_size", d.Width, d.Height);
        progGradient.Uniform("u_gain", bake.Gain);
        progGradient.Uniform("u_maxSlope", bake.MaxSlope);
        progGradient.Uniform2f("u_edgeT", bake.EdgeT0, bake.EdgeT1);
        DrawFullscreenTriangle();
    }

    private static void RunDivergence(DynamicTexture g, DynamicTexture dstDiv)
    {
        BindTarget(dstDiv);
        progDivergence!.Use();
        progDivergence.BindTexture2D("u_g", g.TextureId, 0);
        progDivergence.Uniform2i("u_size", dstDiv.Width, dstDiv.Height);
        DrawFullscreenTriangle();
    }

    private static void RunNormalize(DynamicTexture h, DynamicTexture dst, float mean, float heightStrength, float gamma)
    {
        BindTarget(dst);
        progNormalize!.Use();
        progNormalize.BindTexture2D("u_h", h.TextureId, 0);
        progNormalize.Uniform2i("u_size", dst.Width, dst.Height);
        progNormalize.Uniform("u_mean", mean);
        progNormalize.Uniform("u_heightStrength", heightStrength);
        progNormalize.Uniform("u_gamma", gamma);
        DrawFullscreenTriangle();
    }

    private static void RunPackToAtlas(
        int dstAtlasTexId,
        (int x, int y) viewportOriginPx,
        (int w, int h) tileSizePx,
        (int w, int h) solverSizePx,
        DynamicTexture heightTex,
        float normalStrength)
    {
        // Render into atlas sidecar, restricting to this tile rect via viewport.
        BindAtlasTarget(dstAtlasTexId, viewportOriginPx.x, viewportOriginPx.y, tileSizePx.w, tileSizePx.h);
        progPackToAtlas!.Use();
        progPackToAtlas.BindTexture2D("u_height", heightTex.TextureId, 0);
        progPackToAtlas.Uniform2i("u_solverSize", solverSizePx.w, solverSizePx.h);
        progPackToAtlas.Uniform2i("u_tileSize", tileSizePx.w, tileSizePx.h);
        progPackToAtlas.Uniform2i("u_viewportOrigin", viewportOriginPx.x, viewportOriginPx.y);
        progPackToAtlas.Uniform("u_normalStrength", normalStrength);
        DrawFullscreenTriangle();
    }

    private static int ComputeRadius(float sigma)
    {
        int r = (int)Math.Ceiling(3.0 * sigma);
        if (r > MaxRadius) r = MaxRadius;
        return r;
    }

    private static float[] BuildGaussianWeights(float sigma, int radius)
    {
        // weights[0..radius]
        float[] w = new float[MaxRadius + 1];
        float twoSigma2 = 2f * sigma * sigma;
        float sum = 0f;

        for (int i = 0; i <= radius; i++)
        {
            float x = i;
            float v = (float)Math.Exp(-(x * x) / twoSigma2);
            w[i] = v;
            sum += (i == 0) ? v : (2f * v);
        }

        float inv = sum > 0f ? (1f / sum) : 1f;
        for (int i = 0; i <= radius; i++)
        {
            w[i] *= inv;
        }

        return w;
    }

    private static float ComputeMeanR32f(DynamicTexture tex)
    {
        float[] data = tex.ReadPixels();
        if (data.Length == 0) return 0f;

        // R32F: one float per pixel.
        double sum = 0;
        for (int i = 0; i < data.Length; i++) sum += data[i];
        return (float)(sum / data.Length);
    }

    private sealed class TileResources
    {
        public DynamicTexture L { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_L");
        public DynamicTexture Base { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_Base");
        public DynamicTexture D0 { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_D0");
        public DynamicTexture G1 { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_G1");
        public DynamicTexture G2 { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_G2");
        public DynamicTexture G3 { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_G3");
        public DynamicTexture G4 { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_G4");
        public DynamicTexture D { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_D");
        public DynamicTexture G { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.Rg32f, TextureFilterMode.Nearest, "vge_bake_Gxy");
        public DynamicTexture Div { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_Div");
        public DynamicTexture H { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_H");
        public DynamicTexture Hn { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_Hn");
        public DynamicTexture Tmp { get; private set; } = DynamicTexture.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_Tmp");

        public void EnsureSize(int w, int h)
        {
            ResizeIfNeeded(L, w, h);
            ResizeIfNeeded(Base, w, h);
            ResizeIfNeeded(D0, w, h);
            ResizeIfNeeded(G1, w, h);
            ResizeIfNeeded(G2, w, h);
            ResizeIfNeeded(G3, w, h);
            ResizeIfNeeded(G4, w, h);
            ResizeIfNeeded(D, w, h);
            ResizeIfNeeded(G, w, h);
            ResizeIfNeeded(Div, w, h);
            ResizeIfNeeded(H, w, h);
            ResizeIfNeeded(Hn, w, h);
            ResizeIfNeeded(Tmp, w, h);
        }

        private static void ResizeIfNeeded(DynamicTexture tex, int w, int h)
        {
            if (tex.Width == w && tex.Height == h) return;
            tex.Resize(w, h);
        }
    }

    private sealed class MultigridResources
    {
        private int w;
        private int h;
        private int levels;
        private (int w, int h)[] levelSizes = Array.Empty<(int w, int h)>();

        private DynamicTexture[] hLevel = Array.Empty<DynamicTexture>();
        private DynamicTexture[] hTmpLevel = Array.Empty<DynamicTexture>();
        private DynamicTexture[] bLevel = Array.Empty<DynamicTexture>();
        private DynamicTexture[] residualLevel = Array.Empty<DynamicTexture>();

        public void EnsureSize(int width, int height)
        {
            if (w == width && h == height && levels > 0)
            {
                return;
            }

            w = width;
            h = height;

            // Allow non-power-of-two by halving with ceil.
            var sizes = new List<(int w, int h)>(capacity: 12);
            int lw = w;
            int lh = h;
            sizes.Add((lw, lh));

            while (lw > 32 || lh > 32)
            {
                lw = Math.Max(1, (lw + 1) / 2);
                lh = Math.Max(1, (lh + 1) / 2);
                sizes.Add((lw, lh));
                if (lw == 1 && lh == 1) break;
            }

            levels = sizes.Count;
            levelSizes = sizes.ToArray();

            DisposeAll();

            hLevel = new DynamicTexture[levels];
            hTmpLevel = new DynamicTexture[levels];
            bLevel = new DynamicTexture[levels];
            residualLevel = new DynamicTexture[levels];

            for (int l = 0; l < levels; l++)
            {
                (int levelW, int levelH) = levelSizes[l];
                hLevel[l] = DynamicTexture.Create(levelW, levelH, PixelInternalFormat.R32f, TextureFilterMode.Nearest, $"vge_mg_h_{levelW}x{levelH}");
                hTmpLevel[l] = DynamicTexture.Create(levelW, levelH, PixelInternalFormat.R32f, TextureFilterMode.Nearest, $"vge_mg_htmp_{levelW}x{levelH}");
                bLevel[l] = DynamicTexture.Create(levelW, levelH, PixelInternalFormat.R32f, TextureFilterMode.Nearest, $"vge_mg_b_{levelW}x{levelH}");
                residualLevel[l] = DynamicTexture.Create(levelW, levelH, PixelInternalFormat.R32f, TextureFilterMode.Nearest, $"vge_mg_r_{levelW}x{levelH}");
            }
        }

        public void Solve(DynamicTexture rhs, DynamicTexture outH, LumOnConfig.NormalDepthBakeConfig bake)
        {
            // Copy rhs into bLevel[0] (keep a stable reference for residual).
            RunCopy(rhs, bLevel[0]);

            // Clear solution guess.
            ClearR32f(hLevel[0], 0f);
            ClearR32f(hTmpLevel[0], 0f);

            for (int cycle = 0; cycle < bake.MultigridVCycles; cycle++)
            {
                VCycle(0, bake);
            }

            // Copy final hLevel[0] into outH.
            RunCopy(hLevel[0], outH);
        }

        private void VCycle(int level, LumOnConfig.NormalDepthBakeConfig bake)
        {
            // Pre-smooth.
            for (int i = 0; i < bake.MultigridPreSmooth; i++)
            {
                RunJacobi(hLevel[level], bLevel[level], hTmpLevel[level]);
                Swap(ref hLevel[level], ref hTmpLevel[level]);
            }

            // Residual: r = b - A*h.
            RunResidual(hLevel[level], bLevel[level], residualLevel[level]);

            bool isCoarsest = level == levels - 1;
            if (!isCoarsest)
            {
                // Restrict residual to coarse RHS.
                RunRestrict(residualLevel[level], bLevel[level + 1]);

                // Clear coarse error guess.
                ClearR32f(hLevel[level + 1], 0f);
                ClearR32f(hTmpLevel[level + 1], 0f);

                VCycle(level + 1, bake);

                // Prolongate coarse error and add to fine.
                RunProlongateAdd(hLevel[level], hLevel[level + 1], hTmpLevel[level]);
                Swap(ref hLevel[level], ref hTmpLevel[level]);

                // Post-smooth.
                for (int i = 0; i < bake.MultigridPostSmooth; i++)
                {
                    RunJacobi(hLevel[level], bLevel[level], hTmpLevel[level]);
                    Swap(ref hLevel[level], ref hTmpLevel[level]);
                }
            }
            else
            {
                // Coarsest: iterate more.
                for (int i = 0; i < bake.MultigridCoarsestIters; i++)
                {
                    RunJacobi(hLevel[level], bLevel[level], hTmpLevel[level]);
                    Swap(ref hLevel[level], ref hTmpLevel[level]);
                }
            }
        }

        private static void Swap(ref DynamicTexture a, ref DynamicTexture b)
        {
            (a, b) = (b, a);
        }

        private static void ClearR32f(DynamicTexture tex, float v)
        {
            BindTarget(tex);
            GL.ClearColor(v, 0f, 0f, 0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        // Uses outer RunCopy

        private static void RunJacobi(DynamicTexture h, DynamicTexture b, DynamicTexture dst)
        {
            BindTarget(dst);
            progJacobi!.Use();
            progJacobi.BindTexture2D("u_h", h.TextureId, 0);
            progJacobi.BindTexture2D("u_b", b.TextureId, 1);
            progJacobi.Uniform2i("u_size", dst.Width, dst.Height);
            DrawFullscreenTriangle();
        }

        private static void RunResidual(DynamicTexture h, DynamicTexture b, DynamicTexture dst)
        {
            BindTarget(dst);
            progResidual!.Use();
            progResidual.BindTexture2D("u_h", h.TextureId, 0);
            progResidual.BindTexture2D("u_b", b.TextureId, 1);
            progResidual.Uniform2i("u_size", dst.Width, dst.Height);
            DrawFullscreenTriangle();
        }

        private static void RunRestrict(DynamicTexture fine, DynamicTexture coarse)
        {
            BindTarget(coarse);
            progRestrict!.Use();
            progRestrict.BindTexture2D("u_fine", fine.TextureId, 0);
            progRestrict.Uniform2i("u_fineSize", fine.Width, fine.Height);
            progRestrict.Uniform2i("u_coarseSize", coarse.Width, coarse.Height);
            DrawFullscreenTriangle();
        }

        private static void RunProlongateAdd(DynamicTexture fineH, DynamicTexture coarseE, DynamicTexture dst)
        {
            BindTarget(dst);
            progProlongateAdd!.Use();
            progProlongateAdd.BindTexture2D("u_fineH", fineH.TextureId, 0);
            progProlongateAdd.BindTexture2D("u_coarseE", coarseE.TextureId, 1);
            progProlongateAdd.Uniform2i("u_fineSize", fineH.Width, fineH.Height);
            progProlongateAdd.Uniform2i("u_coarseSize", coarseE.Width, coarseE.Height);
            DrawFullscreenTriangle();
        }

        private void DisposeAll()
        {
            foreach (DynamicTexture t in hLevel) t.Dispose();
            foreach (DynamicTexture t in hTmpLevel) t.Dispose();
            foreach (DynamicTexture t in bLevel) t.Dispose();
            foreach (DynamicTexture t in residualLevel) t.Dispose();

            hLevel = Array.Empty<DynamicTexture>();
            hTmpLevel = Array.Empty<DynamicTexture>();
            bLevel = Array.Empty<DynamicTexture>();
            residualLevel = Array.Empty<DynamicTexture>();
        }
    }
}
