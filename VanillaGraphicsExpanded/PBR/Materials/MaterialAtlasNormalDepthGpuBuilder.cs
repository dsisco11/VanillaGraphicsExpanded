using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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
internal static class MaterialAtlasNormalDepthGpuBuilder
{
    private const int MinBakeTilePx = 2;
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
    private const int MaxDebugTilesPerPage = 3;
    private const int FlatnessSampleStride = 128;
    private const float FlatnessVarianceEpsilon = 0.0025f;
    private const float SaturationEpsilon = 0.01f;

    private static float ClampSigmaToTile(float sigma, int tileW, int tileH, float maxFractionOfMinDim)
    {
        if (sigma <= 0f) return 0f;
        float minDim = Math.Min(tileW, tileH);
        float maxSigma = Math.Max(0.5f, minDim * maxFractionOfMinDim);
        return sigma > maxSigma ? maxSigma : sigma;
    }

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

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

        // Engine GL state can leak into the bake. Force a known-good state and restore after.
        bool prevBlend = GL.IsEnabled(EnableCap.Blend);
        bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool prevScissor = GL.IsEnabled(EnableCap.ScissorTest);
        bool prevCull = GL.IsEnabled(EnableCap.CullFace);
        GL.GetBoolean(GetPName.ColorWritemask, out bool prevColorMaskR);
        // Note: GetBoolean(ColorWritemask) returns 4 values in native OpenGL; OpenTK overload differs.
        // We'll restore via GetBooleanv to be safe.
        bool[] prevColorMask = new bool[4];
        GL.GetBoolean(GetPName.ColorWritemask, prevColorMask);

        try
        {
            // Known-good state for offscreen full-screen passes.
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.CullFace);
            GL.ColorMask(true, true, true, true);

            scratchFbo!.Bind();
            GL.BindVertexArray(vao);

            // Clear entire atlas sidecar first (deterministic baseline).
            ClearAtlasPageUnsafe(destNormalDepthTexId, atlasWidth, atlasHeight);

            int debugTilesLogged = 0;
            int bakedRects = 0;
            int skippedInvalidRects = 0;
            int skippedTinyRects = 0;
            int sampledRects = 0;
            int flatSampledRects = 0;
            int flatSampledBlackRects = 0;
            int flatSampledWhiteRects = 0;
            int flatDetailsLogged = 0;

            foreach (TextureAtlasPosition pos in positions)
            {
                if (!TryGetRectPx(pos, atlasWidth, atlasHeight, out int rx, out int ry, out int rw, out int rh))
                {
                    skippedInvalidRects++;
                    continue;
                }

                // Extremely small tiles can't produce stable gradients; leave defaults.
                // Note: leaving defaults means neutral height (0.5 encoded) and flat normal.
                if (rw < MinBakeTilePx || rh < MinBakeTilePx)
                {
                    skippedTinyRects++;
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

                // Adaptive clamp: SigmaBig as configured can be too aggressive for small tiles (e.g. 32x32),
                // wiping out most of the low/medium-frequency structure and yielding a flat (neutral) height map.
                // Clamp to a fraction of the tile size to preserve usable signal across more atlas rects.
                float sigmaBig = ClampSigmaToTile(bake.SigmaBig, solverW, solverH, maxFractionOfMinDim: 0.25f);

                // Likewise clamp the band-pass sigmas so small tiles don't end up with multiple passes
                // that are effectively "global" blurs.
                float sigma1 = ClampSigmaToTile(bake.Sigma1, solverW, solverH, maxFractionOfMinDim: 0.05f);
                float sigma2 = ClampSigmaToTile(bake.Sigma2, solverW, solverH, maxFractionOfMinDim: 0.08f);
                float sigma3 = ClampSigmaToTile(bake.Sigma3, solverW, solverH, maxFractionOfMinDim: 0.12f);
                float sigma4 = ClampSigmaToTile(bake.Sigma4, solverW, solverH, maxFractionOfMinDim: 0.20f);

                // Other rect-size affected knobs (gradient stage operates in texel space).
                // Scale relative to a 32x32 baseline and clamp to avoid extreme changes.
                float minDim = Math.Min(solverW, solverH);
                float sizeScale = Clamp(32f / Math.Max(1f, minDim), 0.5f, 2.0f);
                float gain = bake.Gain * sizeScale;
                float maxSlope = bake.MaxSlope * sizeScale;
                float edgeT0 = bake.EdgeT0 * sizeScale;
                float edgeT1 = bake.EdgeT1 * sizeScale;

                // Relative-contrast D tends to have smaller gradient magnitudes than absolute luminance D.
                // If we keep the same edge thresholds, many tiles end up with edge==0 and therefore g==0.
                // Lower thresholds in this mode to preserve detail.
                const float RelContrastEdgeScale = 0.25f;
                edgeT0 *= RelContrastEdgeScale;
                edgeT1 *= RelContrastEdgeScale;

                if (cfg.DebugLogNormalDepthAtlas && debugTilesLogged < MaxDebugTilesPerPage)
                {
                    capi.Logger.Debug(
                    "[VGE] Normal+depth effective params: rect=({0},{1},{2},{3}) sigmaBig={4:0.00} sigma1={5:0.00} sigma2={6:0.00} sigma3={7:0.00} sigma4={8:0.00} gain={9:0.00} maxSlope={10:0.00} edgeT=({11:0.0000},{12:0.0000})",
                        rx,
                        ry,
                        rw,
                        rh,
                        sigmaBig,
                        sigma1,
                        sigma2,
                        sigma3,
                    sigma4,
                    gain,
                    maxSlope,
                    edgeT0,
                    edgeT1);
                }

                // 1) Luminance (linear) from atlas sub-rect.
                RunLuminancePass(
                    atlasTexId: baseAlbedoAtlasPageTexId,
                    atlasRectPx: (rx, ry, rw, rh),
                    dst: tile.L);

                // 2) Remove low-frequency ramps: base = Gauss(L, sigmaBig), D0 = L - base.
                RunGaussian(tile.L, tile.Tmp, tile.Base, sigmaBig);
                // Use relative contrast to reduce sensitivity to tint/brightness:
                // D0 = (L - base) / (base + eps)
                RunSub(tile.L, tile.Base, tile.D0, relContrast: true);

                // 3) Multi-scale band-pass on D0.
                RunGaussian(tile.D0, tile.Tmp, tile.G1, sigma1);
                RunGaussian(tile.D0, tile.Tmp, tile.G2, sigma2);
                RunGaussian(tile.D0, tile.Tmp, tile.G3, sigma3);
                RunGaussian(tile.D0, tile.Tmp, tile.G4, sigma4);
                RunCombine(tile.G1, tile.G2, tile.G3, tile.G4, tile.D, bake.W1, bake.W2, bake.W3);

                // 4) Desired gradient field.
                RunGradient(tile.D, tile.G, gain, maxSlope, edgeT0, edgeT1);

                // 5) Divergence.
                RunDivergence(tile.G, tile.Div);

                // 6) Multigrid Poisson solve: Δh = div (periodic).
                mg.Solve(tile.Div, tile.H, bake);

                // 7) Subtract mean on CPU (fix DC offset) then normalize/gamma on GPU.
                var (mean, center, minH, maxH) = ComputeStatsR32f(tile.H);

                // Always normalize the solved height field per tile, but do so asymmetrically:
                // scale negatives by (center-min), positives by (max-center).
                // This avoids skewed tiles becoming "mostly black" (or "mostly white") after packing.
                const float Eps = 1e-6f;
                const float MaxInv = 64f;
                float negSpan = center - minH;
                float posSpan = maxH - center;
                float invNeg = negSpan > Eps ? Math.Min(1f / negSpan, MaxInv) : 0f;
                float invPos = posSpan > Eps ? Math.Min(1f / posSpan, MaxInv) : 0f;

                RunNormalize(tile.H, tile.Hn, center, invNeg, invPos, bake.HeightStrength, bake.Gamma);

                // 8) Pack to atlas (RGB normal in 0..1, A signed height).
                RunPackToAtlas(
                    dstAtlasTexId: destNormalDepthTexId,
                    viewportOriginPx: (rx, ry),
                    tileSizePx: (rw, rh),
                    solverSizePx: (solverW, solverH),
                    heightTex: tile.Hn,
                    baseAlbedoAtlasTexId: baseAlbedoAtlasPageTexId,
                    normalStrength: bake.NormalStrength,
                    normalScale: 1f,
                    depthScale: 1f);

                bakedRects++;

                // Lightweight: sample every Nth baked rect to estimate how many are effectively flat.
                if (cfg.DebugLogNormalDepthAtlas && (bakedRects % FlatnessSampleStride) == 0)
                {
                    int sx = rx + rw / 2;
                    int sy = ry + rh / 2;
                    float aCenter = ReadAtlasPixelRgba(destNormalDepthTexId, sx, sy).a;
                    float a00 = ReadAtlasPixelRgba(destNormalDepthTexId, rx + 0, ry + 0).a;
                    float a10 = ReadAtlasPixelRgba(destNormalDepthTexId, rx + (rw - 1), ry + 0).a;
                    float a01 = ReadAtlasPixelRgba(destNormalDepthTexId, rx + 0, ry + (rh - 1)).a;
                    float a11 = ReadAtlasPixelRgba(destNormalDepthTexId, rx + (rw - 1), ry + (rh - 1)).a;

                    float minA = Math.Min(aCenter, Math.Min(Math.Min(a00, a10), Math.Min(a01, a11)));
                    float maxA = Math.Max(aCenter, Math.Max(Math.Max(a00, a10), Math.Max(a01, a11)));
                    float spanA = maxA - minA;

                    sampledRects++;
                    if (spanA <= FlatnessVarianceEpsilon)
                    {
                        flatSampledRects++;
                        if (maxA <= SaturationEpsilon) flatSampledBlackRects++;
                        if (minA >= (1f - SaturationEpsilon)) flatSampledWhiteRects++;

                        if (flatDetailsLogged < MaxDebugTilesPerPage)
                        {
                            // Sample the base albedo atlas at matching pixels to see if the source tile itself is flat.
                            var cCenter = ReadAtlasPixelRgba(baseAlbedoAtlasPageTexId, sx, sy);
                            var c00 = ReadAtlasPixelRgba(baseAlbedoAtlasPageTexId, rx + 0, ry + 0);
                            var c10 = ReadAtlasPixelRgba(baseAlbedoAtlasPageTexId, rx + (rw - 1), ry + 0);
                            var c01 = ReadAtlasPixelRgba(baseAlbedoAtlasPageTexId, rx + 0, ry + (rh - 1));
                            var c11 = ReadAtlasPixelRgba(baseAlbedoAtlasPageTexId, rx + (rw - 1), ry + (rh - 1));

                            float L((float r, float g, float b) rgb) => 0.2126f * rgb.r + 0.7152f * rgb.g + 0.0722f * rgb.b;
                            float lCenter = L(cCenter.rgb);
                            float l00 = L(c00.rgb);
                            float l10 = L(c10.rgb);
                            float l01 = L(c01.rgb);
                            float l11 = L(c11.rgb);

                            capi.Logger.Debug(
                                "[VGE] Normal+depth flat-tile sample: rect=({0},{1},{2},{3}) pack.a=[min={4:0.000},max={5:0.000},span={6:0.000},center={7:0.000}] albedoL=[{8:0.000},{9:0.000},{10:0.000},{11:0.000},{12:0.000}] albedoA=[{13:0.000},{14:0.000},{15:0.000},{16:0.000},{17:0.000}]",
                                rx,
                                ry,
                                rw,
                                rh,
                                minA,
                                maxA,
                                spanA,
                                aCenter,
                                lCenter,
                                l00,
                                l10,
                                l01,
                                l11,
                                cCenter.a,
                                c00.a,
                                c10.a,
                                c01.a,
                                c11.a);

                            flatDetailsLogged++;
                        }
                    }
                }

                if (cfg.DebugLogNormalDepthAtlas)
                {
                    int sx = rx + rw / 2;
                    int sy = ry + rh / 2;
                    var (a, rgb) = ReadAtlasPixelRgba(destNormalDepthTexId, sx, sy);

                    if (debugTilesLogged < MaxDebugTilesPerPage)
                    {
                        // Also sample intermediate R32F tiles so we can locate where saturation happens.
                        // Note: tile textures are tile-local coordinates.
                        int tx = solverW / 2;
                        int ty = solverH / 2;
                        float h = ReadTexturePixelR32f(tile.H, tx, ty);
                        float hn = ReadTexturePixelR32f(tile.Hn, tx, ty);

                        // Sample a few more points to detect "binary alpha" vs real gradients.
                        // Atlas coords use the same (x,y) convention as glReadPixels (bottom-left origin).
                        float a00 = ReadAtlasPixelRgba(destNormalDepthTexId, rx + 0, ry + 0).a;
                        float a10 = ReadAtlasPixelRgba(destNormalDepthTexId, rx + (rw - 1), ry + 0).a;
                        float a01 = ReadAtlasPixelRgba(destNormalDepthTexId, rx + 0, ry + (rh - 1)).a;
                        float a11 = ReadAtlasPixelRgba(destNormalDepthTexId, rx + (rw - 1), ry + (rh - 1)).a;

                        float hn00 = ReadTexturePixelR32f(tile.Hn, 0, 0);
                        float hn10 = ReadTexturePixelR32f(tile.Hn, solverW - 1, 0);
                        float hn01 = ReadTexturePixelR32f(tile.Hn, 0, solverH - 1);
                        float hn11 = ReadTexturePixelR32f(tile.Hn, solverW - 1, solverH - 1);

                        capi.Logger.Debug(
                            "[VGE] Normal+depth tile debug: atlas={0} rect=({1},{2},{3},{4}) samplePx=({5},{6}) H={7:0.0000} mean={8:0.0000} center={9:0.0000} min={10:0.0000} max={11:0.0000} invNeg={12:0.000} invPos={13:0.000} Hn={14:0.0000} (strength={15:0.00}, gamma={16:0.00}) pack.a={17:0.000} pack.rgb=({18:0.000},{19:0.000},{20:0.000})",
                            baseAlbedoAtlasPageTexId,
                            rx,
                            ry,
                            rw,
                            rh,
                            sx,
                            sy,
                            h,
                            mean,
                            center,
                            minH,
                            maxH,
                            invNeg,
                            invPos,
                            hn,
                            bake.HeightStrength,
                            bake.Gamma,
                            a,
                            rgb.r,
                            rgb.g,
                            rgb.b);

                        capi.Logger.Debug(
                            "[VGE] Normal+depth tile samples: pack.a corners=[{0:0.000},{1:0.000},{2:0.000},{3:0.000}] Hn corners=[{4:0.0000},{5:0.0000},{6:0.0000},{7:0.0000}]",
                            a00,
                            a10,
                            a01,
                            a11,
                            hn00,
                            hn10,
                            hn01,
                            hn11);
                        debugTilesLogged++;
                    }
                }
            }

            if (ConfigModSystem.Config.DebugLogNormalDepthAtlas &&
                (skippedTinyRects != 0 || skippedInvalidRects != 0 || flatSampledRects != 0))
            {
                capi.Logger.Debug(
                    "[VGE] Normal+depth atlas bake summary: atlas={0} rects={1} baked={2} skippedTiny(<2px)={3} skippedInvalid={4} sampledEvery={5} sampled={6} sampledFlat(spanA<={7})={8} flatBlack(a<={9})={10} flatWhite(a>={11})={12}",
                    baseAlbedoAtlasPageTexId,
                    positions.Length,
                    bakedRects,
                    skippedTinyRects,
                    skippedInvalidRects,
                    FlatnessSampleStride,
                    sampledRects,
                    FlatnessVarianceEpsilon,
                    flatSampledRects,
                    SaturationEpsilon,
                    flatSampledBlackRects,
                    1f - SaturationEpsilon,
                    flatSampledWhiteRects);
            }
        }
        catch
        {
            // Best-effort: on failure, the sidecar remains at default clear values.
        }
        finally
        {
            // CRITICAL: ensure Vintagestory's shader program tracking is not left in a dirty state.
            // If any of our VGE programs remains "in use", the engine will throw:
            // "Already a different shader (...) in use!" on the next render pass.
            StopAllBakerPrograms();

            GL.UseProgram(prevProgram);
            GL.BindVertexArray(prevVao);
                GBuffer.RestoreBinding(prevFbo);
            GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);

            GL.ActiveTexture((TextureUnit)prevActiveTex);
            GL.BindTexture(TextureTarget.Texture2D, prevTex2D);

            if (prevBlend) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
            if (prevDepthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
            if (prevScissor) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
            if (prevCull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            GL.ColorMask(prevColorMask[0], prevColorMask[1], prevColorMask[2], prevColorMask[3]);
        }
    }

    /// <summary>
    /// Clears an entire normal+depth atlas page to neutral defaults.
    /// Intended to be called once per page before any per-rect bakes.
    /// </summary>
    public static void ClearAtlasPage(
        ICoreClientAPI capi,
        int destNormalDepthTexId,
        int atlasWidth,
        int atlasHeight)
    {
        ArgumentNullException.ThrowIfNull(capi);
        if (destNormalDepthTexId == 0) throw new ArgumentOutOfRangeException(nameof(destNormalDepthTexId));
        if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));

        try
        {
            EnsureInitialized(capi);
        }
        catch
        {
            return;
        }

        int[] prevViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, prevViewport);
        int prevFbo = GBuffer.SaveBinding();
        GL.GetInteger(GetPName.VertexArrayBinding, out int prevVao);
        GL.GetInteger(GetPName.CurrentProgram, out int prevProgram);
        GL.GetInteger(GetPName.ActiveTexture, out int prevActiveTex);
        GL.GetInteger(GetPName.TextureBinding2D, out int prevTex2D);

        bool prevBlend = GL.IsEnabled(EnableCap.Blend);
        bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool prevScissor = GL.IsEnabled(EnableCap.ScissorTest);
        bool prevCull = GL.IsEnabled(EnableCap.CullFace);
        bool[] prevColorMask = new bool[4];
        GL.GetBoolean(GetPName.ColorWritemask, prevColorMask);

        try
        {
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.CullFace);
            GL.ColorMask(true, true, true, true);

            scratchFbo!.Bind();
            GL.BindVertexArray(vao);

            ClearAtlasPageUnsafe(destNormalDepthTexId, atlasWidth, atlasHeight);
        }
        catch
        {
            // Best-effort.
        }
        finally
        {
            StopAllBakerPrograms();

            GL.UseProgram(prevProgram);
            GL.BindVertexArray(prevVao);
            GBuffer.RestoreBinding(prevFbo);
            GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);

            GL.ActiveTexture((TextureUnit)prevActiveTex);
            GL.BindTexture(TextureTarget.Texture2D, prevTex2D);

            if (prevBlend) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
            if (prevDepthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
            if (prevScissor) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
            if (prevCull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            GL.ColorMask(prevColorMask[0], prevColorMask[1], prevColorMask[2], prevColorMask[3]);
        }
    }

    /// <summary>
    /// Bakes a single atlas rect into the destination normal+depth atlas.
    /// This does not clear the atlas page; call <see cref="ClearAtlasPage"/> once per page if needed.
    /// </summary>
    public static bool BakePerRect(
        ICoreClientAPI capi,
        int baseAlbedoAtlasPageTexId,
        int destNormalDepthTexId,
        int atlasWidth,
        int atlasHeight,
        int rectX,
        int rectY,
        int rectWidth,
        int rectHeight,
        float normalScale,
        float depthScale)
    {
        ArgumentNullException.ThrowIfNull(capi);
        if (baseAlbedoAtlasPageTexId == 0) throw new ArgumentOutOfRangeException(nameof(baseAlbedoAtlasPageTexId));
        if (destNormalDepthTexId == 0) throw new ArgumentOutOfRangeException(nameof(destNormalDepthTexId));
        if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));
        if (rectX < 0 || rectY < 0) throw new ArgumentOutOfRangeException("rect origin must be non-negative");
        if (rectWidth <= 0 || rectHeight <= 0) throw new ArgumentOutOfRangeException("rect size must be positive");
        if (rectX + rectWidth > atlasWidth || rectY + rectHeight > atlasHeight) throw new ArgumentOutOfRangeException("rect exceeds atlas bounds");

        if (rectWidth < MinBakeTilePx || rectHeight < MinBakeTilePx)
        {
            return false;
        }

        if (float.IsNaN(normalScale) || float.IsInfinity(normalScale) || normalScale < 0f) normalScale = 1f;
        if (float.IsNaN(depthScale) || float.IsInfinity(depthScale) || depthScale < 0f) depthScale = 1f;

        try
        {
            EnsureInitialized(capi);
        }
        catch
        {
            return false;
        }

        int[] prevViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, prevViewport);
        int prevFbo = GBuffer.SaveBinding();
        GL.GetInteger(GetPName.VertexArrayBinding, out int prevVao);
        GL.GetInteger(GetPName.CurrentProgram, out int prevProgram);
        GL.GetInteger(GetPName.ActiveTexture, out int prevActiveTex);
        GL.GetInteger(GetPName.TextureBinding2D, out int prevTex2D);

        bool prevBlend = GL.IsEnabled(EnableCap.Blend);
        bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool prevScissor = GL.IsEnabled(EnableCap.ScissorTest);
        bool prevCull = GL.IsEnabled(EnableCap.CullFace);
        bool[] prevColorMask = new bool[4];
        GL.GetBoolean(GetPName.ColorWritemask, prevColorMask);

        try
        {
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.CullFace);
            GL.ColorMask(true, true, true, true);

            scratchFbo!.Bind();
            GL.BindVertexArray(vao);

            int solverW = rectWidth;
            int solverH = rectHeight;

            tile ??= new TileResources();
            tile.EnsureSize(solverW, solverH);

            mg ??= new MultigridResources();
            mg.EnsureSize(solverW, solverH);

            var cfg = ConfigModSystem.Config;
            var bake = cfg.NormalDepthBake;

            float sigmaBig = ClampSigmaToTile(bake.SigmaBig, solverW, solverH, maxFractionOfMinDim: 0.25f);
            float sigma1 = ClampSigmaToTile(bake.Sigma1, solverW, solverH, maxFractionOfMinDim: 0.05f);
            float sigma2 = ClampSigmaToTile(bake.Sigma2, solverW, solverH, maxFractionOfMinDim: 0.08f);
            float sigma3 = ClampSigmaToTile(bake.Sigma3, solverW, solverH, maxFractionOfMinDim: 0.12f);
            float sigma4 = ClampSigmaToTile(bake.Sigma4, solverW, solverH, maxFractionOfMinDim: 0.20f);

            float minDim = Math.Min(solverW, solverH);
            float sizeScale = Clamp(32f / Math.Max(1f, minDim), 0.5f, 2.0f);
            float gain = bake.Gain * sizeScale;
            float maxSlope = bake.MaxSlope * sizeScale;
            float edgeT0 = bake.EdgeT0 * sizeScale;
            float edgeT1 = bake.EdgeT1 * sizeScale;

            const float RelContrastEdgeScale = 0.25f;
            edgeT0 *= RelContrastEdgeScale;
            edgeT1 *= RelContrastEdgeScale;

            RunLuminancePass(
                atlasTexId: baseAlbedoAtlasPageTexId,
                atlasRectPx: (rectX, rectY, rectWidth, rectHeight),
                dst: tile.L);

            RunGaussian(tile.L, tile.Tmp, tile.Base, sigmaBig);
            RunSub(tile.L, tile.Base, tile.D0, relContrast: true);

            RunGaussian(tile.D0, tile.Tmp, tile.G1, sigma1);
            RunGaussian(tile.D0, tile.Tmp, tile.G2, sigma2);
            RunGaussian(tile.D0, tile.Tmp, tile.G3, sigma3);
            RunGaussian(tile.D0, tile.Tmp, tile.G4, sigma4);
            RunCombine(tile.G1, tile.G2, tile.G3, tile.G4, tile.D, bake.W1, bake.W2, bake.W3);

            RunGradient(tile.D, tile.G, gain, maxSlope, edgeT0, edgeT1);
            RunDivergence(tile.G, tile.Div);
            mg.Solve(tile.Div, tile.H, bake);

            var (_, center, minH, maxH) = ComputeStatsR32f(tile.H);

            const float Eps = 1e-6f;
            const float MaxInv = 64f;
            float negSpan = center - minH;
            float posSpan = maxH - center;
            float invNeg = negSpan > Eps ? Math.Min(1f / negSpan, MaxInv) : 0f;
            float invPos = posSpan > Eps ? Math.Min(1f / posSpan, MaxInv) : 0f;

            RunNormalize(tile.H, tile.Hn, center, invNeg, invPos, bake.HeightStrength, bake.Gamma);

            RunPackToAtlas(
                dstAtlasTexId: destNormalDepthTexId,
                viewportOriginPx: (rectX, rectY),
                tileSizePx: (rectWidth, rectHeight),
                solverSizePx: (solverW, solverH),
                heightTex: tile.Hn,
                baseAlbedoAtlasTexId: baseAlbedoAtlasPageTexId,
                normalStrength: bake.NormalStrength,
                normalScale: normalScale,
                depthScale: depthScale);

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            StopAllBakerPrograms();

            GL.UseProgram(prevProgram);
            GL.BindVertexArray(prevVao);
            GBuffer.RestoreBinding(prevFbo);
            GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);

            GL.ActiveTexture((TextureUnit)prevActiveTex);
            GL.BindTexture(TextureTarget.Texture2D, prevTex2D);

            if (prevBlend) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
            if (prevDepthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
            if (prevScissor) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
            if (prevCull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            GL.ColorMask(prevColorMask[0], prevColorMask[1], prevColorMask[2], prevColorMask[3]);
        }
    }

    private static void ClearAtlasPageUnsafe(int destNormalDepthTexId, int atlasWidth, int atlasHeight)
    {
        BindAtlasTarget(destNormalDepthTexId, 0, 0, atlasWidth, atlasHeight);
        // Identity defaults: flat normal and zero depth.
        GL.ClearColor(0.5f, 0.5f, 1.0f, 0.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
    }


    private static void StopAllBakerPrograms()
    {
        // Stop() should be a no-op when the program isn't the current active shader,
        // but calling it defensively avoids leaks across exception paths.
        try { progPackToAtlas?.Stop(); } catch { }
        try { progNormalize?.Stop(); } catch { }
        try { progProlongateAdd?.Stop(); } catch { }
        try { progRestrict?.Stop(); } catch { }
        try { progResidual?.Stop(); } catch { }
        try { progJacobi?.Stop(); } catch { }
        try { progDivergence?.Stop(); } catch { }
        try { progGradient?.Stop(); } catch { }
        try { progCombine?.Stop(); } catch { }
        try { progSub?.Stop(); } catch { }
        try { progGauss1D?.Stop(); } catch { }
        try { progCopy?.Stop(); } catch { }
        try { progLuminance?.Stop(); } catch { }
    }

    private static (float a, (float r, float g, float b) rgb) ReadAtlasPixelRgba(int atlasTexId, int x, int y)
    {
        // Attach the atlas texture to the scratch FBO and read back a single pixel.
        // Note: framebuffer coordinates are bottom-left origin.
        scratchFbo!.AttachColorTextureId(atlasTexId);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        float[] px = new float[4];
        GL.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.Float, px);
        return (px[3], (px[0], px[1], px[2]));
    }

    private static float ReadTexturePixelR32f(DynamicTexture tex, int x, int y)
    {
        scratchFbo!.AttachColor(tex);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        float[] px = new float[1];
        GL.ReadPixels(x, y, 1, 1, PixelFormat.Red, PixelType.Float, px);
        return px[0];
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
        try
        {
            progLuminance.BindTexture2D("u_atlas", atlasTexId, 0);
            progLuminance.Uniform4i("u_atlasRectPx", atlasRectPx.x, atlasRectPx.y, atlasRectPx.w, atlasRectPx.h);
            progLuminance.Uniform2i("u_outSize", dst.Width, dst.Height);
            DrawFullscreenTriangle();
        }
        finally
        {
            progLuminance.Stop();
        }
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
        try
        {
            progGauss1D.BindTexture2D("u_src", src.TextureId, 0);
            progGauss1D.Uniform2i("u_size", src.Width, src.Height);
            progGauss1D.Uniform2i("u_dir", 1, 0);
            progGauss1D.Uniform("u_radius", radius);
            progGauss1D.Uniform1fv("u_weights", weights);
            DrawFullscreenTriangle();
        }
        finally
        {
            progGauss1D.Stop();
        }

        // Vertical
        BindTarget(dst);
        progGauss1D!.Use();
        try
        {
            progGauss1D.BindTexture2D("u_src", tmp.TextureId, 0);
            progGauss1D.Uniform2i("u_size", dst.Width, dst.Height);
            progGauss1D.Uniform2i("u_dir", 0, 1);
            progGauss1D.Uniform("u_radius", radius);
            progGauss1D.Uniform1fv("u_weights", weights);
            DrawFullscreenTriangle();
        }
        finally
        {
            progGauss1D.Stop();
        }
    }

    private static void RunSub(DynamicTexture a, DynamicTexture b, DynamicTexture dst, bool relContrast)
    {
        BindTarget(dst);
        progSub!.Use();
        try
        {
            progSub.BindTexture2D("u_a", a.TextureId, 0);
            progSub.BindTexture2D("u_b", b.TextureId, 1);
            progSub.Uniform2i("u_size", dst.Width, dst.Height);
            progSub.Uniform("u_relContrast", relContrast ? 1 : 0);
            // Small epsilon so multiplicative dark tints don't collapse the signal;
            // clamp range prevents extreme spikes when base is near-zero.
            progSub.Uniform("u_eps", 1e-6f);
            progSub.Uniform("u_vMax", 8f);
            DrawFullscreenTriangle();
        }
        finally
        {
            progSub.Stop();
        }
    }

    private static void RunCopy(DynamicTexture src, DynamicTexture dst)
    {
        BindTarget(dst);
        progCopy!.Use();
        try
        {
            progCopy.BindTexture2D("u_src", src.TextureId, 0);
            progCopy.Uniform2i("u_size", dst.Width, dst.Height);
            DrawFullscreenTriangle();
        }
        finally
        {
            progCopy.Stop();
        }
    }

    private static void RunCombine(DynamicTexture g1, DynamicTexture g2, DynamicTexture g3, DynamicTexture g4, DynamicTexture dst, float w1, float w2, float w3)
    {
        BindTarget(dst);
        progCombine!.Use();
        try
        {
            progCombine.BindTexture2D("u_g1", g1.TextureId, 0);
            progCombine.BindTexture2D("u_g2", g2.TextureId, 1);
            progCombine.BindTexture2D("u_g3", g3.TextureId, 2);
            progCombine.BindTexture2D("u_g4", g4.TextureId, 3);
            progCombine.Uniform3f("u_w", w1, w2, w3);
            progCombine.Uniform2i("u_size", dst.Width, dst.Height);
            DrawFullscreenTriangle();
        }
        finally
        {
            progCombine.Stop();
        }
    }

    private static void RunGradient(DynamicTexture d, DynamicTexture dstG, float gain, float maxSlope, float edgeT0, float edgeT1)
    {
        BindTarget(dstG);
        progGradient!.Use();
        try
        {
            progGradient.BindTexture2D("u_d", d.TextureId, 0);
            progGradient.Uniform2i("u_size", d.Width, d.Height);
            progGradient.Uniform("u_gain", gain);
            progGradient.Uniform("u_maxSlope", maxSlope);
            progGradient.Uniform2f("u_edgeT", edgeT0, edgeT1);
            DrawFullscreenTriangle();
        }
        finally
        {
            progGradient.Stop();
        }
    }

    private static void RunDivergence(DynamicTexture g, DynamicTexture dstDiv)
    {
        BindTarget(dstDiv);
        progDivergence!.Use();
        try
        {
            progDivergence.BindTexture2D("u_g", g.TextureId, 0);
            progDivergence.Uniform2i("u_size", dstDiv.Width, dstDiv.Height);
            DrawFullscreenTriangle();
        }
        finally
        {
            progDivergence.Stop();
        }
    }

    private static void RunNormalize(DynamicTexture h, DynamicTexture dst, float mean, float invNeg, float invPos, float heightStrength, float gamma)
    {
        BindTarget(dst);
        progNormalize!.Use();
        try
        {
            progNormalize.BindTexture2D("u_h", h.TextureId, 0);
            progNormalize.Uniform2i("u_size", dst.Width, dst.Height);
            progNormalize.Uniform("u_mean", mean);
            progNormalize.Uniform("u_invNeg", invNeg);
            progNormalize.Uniform("u_invPos", invPos);
            progNormalize.Uniform("u_heightStrength", heightStrength);
            progNormalize.Uniform("u_gamma", gamma);
            DrawFullscreenTriangle();
        }
        finally
        {
            progNormalize.Stop();
        }
    }

    private static void RunPackToAtlas(
        int dstAtlasTexId,
        (int x, int y) viewportOriginPx,
        (int w, int h) tileSizePx,
        (int w, int h) solverSizePx,
        DynamicTexture heightTex,
        int baseAlbedoAtlasTexId,
        float normalStrength,
        float normalScale,
        float depthScale)
    {
        // Render into atlas sidecar, restricting to this tile rect via viewport.
        BindAtlasTarget(dstAtlasTexId, viewportOriginPx.x, viewportOriginPx.y, tileSizePx.w, tileSizePx.h);
        progPackToAtlas!.Use();
        try
        {
            progPackToAtlas.BindTexture2D("u_height", heightTex.TextureId, 0);
            progPackToAtlas.BindTexture2D("u_albedoAtlas", baseAlbedoAtlasTexId, 1);
            progPackToAtlas.Uniform2i("u_solverSize", solverSizePx.w, solverSizePx.h);
            progPackToAtlas.Uniform2i("u_tileSize", tileSizePx.w, tileSizePx.h);
            progPackToAtlas.Uniform2i("u_viewportOrigin", viewportOriginPx.x, viewportOriginPx.y);
            progPackToAtlas.Uniform("u_normalStrength", normalStrength);
            progPackToAtlas.Uniform("u_normalScale", normalScale);
            progPackToAtlas.Uniform("u_depthScale", depthScale);
            progPackToAtlas.Uniform("u_alphaCutoff", 0.001f);
            DrawFullscreenTriangle();
        }
        finally
        {
            progPackToAtlas.Stop();
        }
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

    private static (float mean, float center, float min, float max) ComputeStatsR32f(DynamicTexture tex)
    {
        float[] data = tex.ReadPixels();
        if (data.Length == 0) return (0f, 0f, 0f, 0f);

        // R32F: one float per pixel.
        // Use SIMD for sum/min/max (single pass) where supported.
        int i = 0;
        int len = data.Length;

        Vector<float> vSum = Vector<float>.Zero;
        Vector<float> vMin = new(float.PositiveInfinity);
        Vector<float> vMax = new(float.NegativeInfinity);

        int vecCount = Vector<float>.Count;
        int lastVec = len - (len % vecCount);
        for (; i < lastVec; i += vecCount)
        {
            var v = new Vector<float>(data, i);
            vSum += v;
            vMin = Vector.Min(vMin, v);
            vMax = Vector.Max(vMax, v);
        }

        double sum = 0;
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;

        for (int lane = 0; lane < vecCount; lane++)
        {
            sum += vSum[lane];
            float mn = vMin[lane];
            float mx = vMax[lane];
            if (mn < min) min = mn;
            if (mx > max) max = mx;
        }

        for (; i < len; i++)
        {
            float v = data[i];
            sum += v;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        int count = data.Length;
        float mean = (float)(sum / count);
        if (float.IsPositiveInfinity(min) || float.IsNegativeInfinity(max))
        {
            min = mean;
            max = mean;
        }

        // Robust center: median (computed in-place via quickselect).
        float center = SelectMedianInPlace(data);
        return (mean, center, min, max);
    }

    private static float SelectMedianInPlace(float[] data)
    {
        int n = data.Length;
        if (n == 0) return 0f;

        int k = n / 2;
        int left = 0;
        int right = n - 1;

        while (true)
        {
            if (left == right) return data[left];

            int pivotIndex = (left + right) >>> 1;
            pivotIndex = Partition(data, left, right, pivotIndex);

            if (k == pivotIndex) return data[k];
            if (k < pivotIndex) right = pivotIndex - 1;
            else left = pivotIndex + 1;
        }
    }

    private static int Partition(float[] data, int left, int right, int pivotIndex)
    {
        float pivotValue = data[pivotIndex];
        (data[pivotIndex], data[right]) = (data[right], data[pivotIndex]);

        int storeIndex = left;
        for (int i = left; i < right; i++)
        {
            if (data[i] < pivotValue)
            {
                (data[storeIndex], data[i]) = (data[i], data[storeIndex]);
                storeIndex++;
            }
        }

        (data[right], data[storeIndex]) = (data[storeIndex], data[right]);
        return storeIndex;
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
            try
            {
                progJacobi.BindTexture2D("u_h", h.TextureId, 0);
                progJacobi.BindTexture2D("u_b", b.TextureId, 1);
                progJacobi.Uniform2i("u_size", dst.Width, dst.Height);
                DrawFullscreenTriangle();
            }
            finally
            {
                progJacobi.Stop();
            }
        }

        private static void RunResidual(DynamicTexture h, DynamicTexture b, DynamicTexture dst)
        {
            BindTarget(dst);
            progResidual!.Use();
            try
            {
                progResidual.BindTexture2D("u_h", h.TextureId, 0);
                progResidual.BindTexture2D("u_b", b.TextureId, 1);
                progResidual.Uniform2i("u_size", dst.Width, dst.Height);
                DrawFullscreenTriangle();
            }
            finally
            {
                progResidual.Stop();
            }
        }

        private static void RunRestrict(DynamicTexture fine, DynamicTexture coarse)
        {
            BindTarget(coarse);
            progRestrict!.Use();
            try
            {
                progRestrict.BindTexture2D("u_fine", fine.TextureId, 0);
                progRestrict.Uniform2i("u_fineSize", fine.Width, fine.Height);
                progRestrict.Uniform2i("u_coarseSize", coarse.Width, coarse.Height);
                DrawFullscreenTriangle();
            }
            finally
            {
                progRestrict.Stop();
            }
        }

        private static void RunProlongateAdd(DynamicTexture fineH, DynamicTexture coarseE, DynamicTexture dst)
        {
            BindTarget(dst);
            progProlongateAdd!.Use();
            try
            {
                progProlongateAdd.BindTexture2D("u_fineH", fineH.TextureId, 0);
                progProlongateAdd.BindTexture2D("u_coarseE", coarseE.TextureId, 1);
                progProlongateAdd.Uniform2i("u_fineSize", fineH.Width, fineH.Height);
                progProlongateAdd.Uniform2i("u_coarseSize", coarseE.Width, coarseE.Height);
                DrawFullscreenTriangle();
            }
            finally
            {
                progProlongateAdd.Stop();
            }
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
