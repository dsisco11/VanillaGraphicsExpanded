using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
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
internal static partial class MaterialAtlasNormalDepthGpuBuilder
{
    private const int MinBakeTilePx = 2;
    // Asset domain for shader source.
    private const string Domain = "vanillagraphicsexpanded";

    // Shader asset names (without extension).
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

    private static PbrHeightBakeParamsUbo? paramsUbo;

    private static bool initialized;
    private static ArrayGraphicsGeometry? geometry;
    private static GpuFramebuffer? scratchFbo;
    private const int MaxDebugTilesPerPage = 3;
    private const int FlatnessSampleStride = 128;
    private const float FlatnessVarianceEpsilon = 0.0025f;
    private const float SaturationEpsilon = 0.01f;

    /// <summary>Bounds blur width by the current tile dimensions.</summary>
    private static float ClampSigmaToTile(float sigma, int tileW, int tileH, float maxFractionOfMinDim)
    {
        if (sigma <= 0f) return 0f;
        float minDim = Math.Min(tileW, tileH);
        float maxSigma = Math.Max(0.5f, minDim * maxFractionOfMinDim);
        return sigma > maxSigma ? maxSigma : sigma;
    }

    /// <summary>Clamps a value to the inclusive supported range.</summary>
    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

    private static PbrHeightBakeShaderProgram? progLuminance;
    private static PbrHeightBakeShaderProgram? progGauss1D;
    private static PbrHeightBakeShaderProgram? progSub;
    private static PbrHeightBakeShaderProgram? progCombine;
    private static PbrHeightBakeShaderProgram? progGradient;
    private static PbrHeightBakeShaderProgram? progDivergence;
    private static PbrHeightBakeShaderProgram? progJacobi;
    private static PbrHeightBakeShaderProgram? progResidual;
    private static PbrHeightBakeShaderProgram? progRestrict;
    private static PbrHeightBakeShaderProgram? progProlongateAdd;
    private static PbrHeightBakeShaderProgram? progNormalize;
    private static PbrHeightBakeShaderProgram? progPackToAtlas;
    private static PbrHeightBakeShaderProgram? progCopy;

    private static PbrHeightBakeParamsUbo Params => paramsUbo ??= new PbrHeightBakeParamsUbo();


    // Intermediate textures are reused and resized per tile.
    private static TileResources? tile;
    private static MultigridResources? mg;

    /// <summary>Bakes each admitted texture rectangle through one restored graphics boundary.</summary>
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

        try
        {
            RunBake(destNormalDepthTexId, draw =>
            {

                // Clear entire atlas sidecar first (deterministic baseline).
                ClearAtlasPageUnsafe(draw, destNormalDepthTexId, atlasWidth, atlasHeight);

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

                    var cfg = ConfigModSystem.Config.MaterialAtlas;
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
                    RunLuminancePass(draw,
                        atlasTexId: baseAlbedoAtlasPageTexId,
                        atlasRectPx: (rx, ry, rw, rh),
                        dst: tile.L);

                    // 2) Remove low-frequency ramps: base = Gauss(L, sigmaBig), D0 = L - base.
                    RunGaussian(draw, tile.L, tile.Tmp, tile.Base, sigmaBig);
                    // Use relative contrast to reduce sensitivity to tint/brightness:
                    // D0 = (L - base) / (base + eps)
                    RunSub(draw, tile.L, tile.Base, tile.D0, relContrast: true);

                    // 3) Multi-scale band-pass on D0.
                    RunGaussian(draw, tile.D0, tile.Tmp, tile.G1, sigma1);
                    RunGaussian(draw, tile.D0, tile.Tmp, tile.G2, sigma2);
                    RunGaussian(draw, tile.D0, tile.Tmp, tile.G3, sigma3);
                    RunGaussian(draw, tile.D0, tile.Tmp, tile.G4, sigma4);
                    RunCombine(draw, tile.G1, tile.G2, tile.G3, tile.G4, tile.D, bake.W1, bake.W2, bake.W3);

                    // 4) Desired gradient field.
                    RunGradient(draw, tile.D, tile.G, gain, maxSlope, edgeT0, edgeT1);

                    // 5) Divergence.
                    RunDivergence(draw, tile.G, tile.Div);

                    // 6) Multigrid Poisson solve: Δh = div (periodic).
                    mg.Solve(draw, tile.Div, tile.H, bake);

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

                    RunNormalize(draw, tile.H, tile.Hn, center, invNeg, invPos, bake.HeightStrength, bake.Gamma);

                    // 8) Pack to atlas (RGB normal in 0..1, A signed height).
                    RunPackToAtlas(draw,
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

                if (ConfigModSystem.Config.MaterialAtlas.DebugLogNormalDepthAtlas &&
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
            });
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            // Preserve the optional bake fallback after safe boundary cleanup.
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

        // Clearing is a resource load operation; it does not require a shader executable.
        using var image = GpuFramebufferAttachment.FromTextureId(destNormalDepthTexId);
        using var target = GpuFramebuffer.CreateEmpty("MaterialAtlas.Clear");
        target.SetAttachment(FramebufferAttachment.ColorAttachment0, image);
        if (!GraphicsCommandContext.TryRun("MaterialAtlas.Clear", [], true, commands =>
        {
            commands.BeginPass(new(target,
                [new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0.5f, 0.5f, 1, 0.5f))],
                area: new(0, 0, atlasWidth, atlasHeight)));
            commands.EndPass();
        })) throw new InvalidOperationException("Atlas clear boundary unavailable.");
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

        try
        {
            return RunBake(destNormalDepthTexId, draw =>
            {

                int solverW = rectWidth;
                int solverH = rectHeight;

                tile ??= new TileResources();
                tile.EnsureSize(solverW, solverH);

                mg ??= new MultigridResources();
                mg.EnsureSize(solverW, solverH);

                var cfg = ConfigModSystem.Config.MaterialAtlas;
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

                RunLuminancePass(draw,
                    atlasTexId: baseAlbedoAtlasPageTexId,
                    atlasRectPx: (rectX, rectY, rectWidth, rectHeight),
                    dst: tile.L);

                RunGaussian(draw, tile.L, tile.Tmp, tile.Base, sigmaBig);
                RunSub(draw, tile.L, tile.Base, tile.D0, relContrast: true);

                RunGaussian(draw, tile.D0, tile.Tmp, tile.G1, sigma1);
                RunGaussian(draw, tile.D0, tile.Tmp, tile.G2, sigma2);
                RunGaussian(draw, tile.D0, tile.Tmp, tile.G3, sigma3);
                RunGaussian(draw, tile.D0, tile.Tmp, tile.G4, sigma4);
                RunCombine(draw, tile.G1, tile.G2, tile.G3, tile.G4, tile.D, bake.W1, bake.W2, bake.W3);

                RunGradient(draw, tile.D, tile.G, gain, maxSlope, edgeT0, edgeT1);
                RunDivergence(draw, tile.G, tile.Div);
                mg.Solve(draw, tile.Div, tile.H, bake);

                var (_, center, minH, maxH) = ComputeStatsR32f(tile.H);

                const float Eps = 1e-6f;
                const float MaxInv = 64f;
                float negSpan = center - minH;
                float posSpan = maxH - center;
                float invNeg = negSpan > Eps ? Math.Min(1f / negSpan, MaxInv) : 0f;
                float invPos = posSpan > Eps ? Math.Min(1f / posSpan, MaxInv) : 0f;

                RunNormalize(draw, tile.H, tile.Hn, center, invNeg, invPos, bake.HeightStrength, bake.Gamma);

                RunPackToAtlas(draw,
                    dstAtlasTexId: destNormalDepthTexId,
                    viewportOriginPx: (rectX, rectY),
                    tileSizePx: (rectWidth, rectHeight),
                    solverSizePx: (solverW, solverH),
                    heightTex: tile.Hn,
                    baseAlbedoAtlasTexId: baseAlbedoAtlasPageTexId,
                    normalStrength: bake.NormalStrength,
                    normalScale: normalScale,
                    depthScale: depthScale);


            });
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            return false;
        }
    }

    /// <summary>Clears the selected atlas within the already established bake boundary.</summary>
    private static void ClearAtlasPageUnsafe(BakeDrawContext draw, int destNormalDepthTexId, int atlasWidth, int atlasHeight)
    {
        draw.SetAtlasTarget(destNormalDepthTexId, 0, 0, atlasWidth, atlasHeight);
        // Identity defaults: flat normal and neutral height (0.5 encoded).
        draw.Clear(0.5f, 0.5f, 1.0f, 0.5f);
    }


    /// <summary>Reads one atlas pixel while preserving framebuffer and pixel-pack bindings.</summary>
    private static (float a, (float r, float g, float b) rgb) ReadAtlasPixelRgba(int atlasTexId, int x, int y)
    {
        // Attach the atlas texture to the scratch FBO and read back a single pixel.
        // Note: framebuffer coordinates are bottom-left origin.
        scratchFbo!.Attach(atlasTexId);
        using var framebuffer = StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, scratchFbo.FboId);
        using var pack = StateCache.Current.SetPixelPackScope(new(1));
        using var transfer = StateCache.Current.BindBufferScope(BufferTarget.PixelPackBuffer, 0);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        float[] px = new float[4];
        GL.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.Float, px);
        return (px[3], (px[0], px[1], px[2]));
    }

    /// <summary>Reads one solver value with explicit tightly packed CPU readback state.</summary>
    private static float ReadTexturePixelR32f(DynamicTexture2D tex, int x, int y)
    {
        scratchFbo!.Attach(tex);
        using var framebuffer = StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, scratchFbo.FboId);
        using var pack = StateCache.Current.SetPixelPackScope(new(1));
        using var transfer = StateCache.Current.BindBufferScope(BufferTarget.PixelPackBuffer, 0);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        float[] px = new float[1];
        GL.ReadPixels(x, y, 1, 1, PixelFormat.Red, PixelType.Float, px);
        return px[0];
    }

    /// <summary>Creates reusable solver programs and geometry before entering a rendering boundary.</summary>
    private static void EnsureInitialized(ICoreClientAPI capi)
    {
        if (initialized)
        {
            return;
        }

        try
        {
            // Minimal GL objects.
            geometry = new(new([]), PrimitiveType.Triangles, new Dictionary<int, GpuVbo>(), 3);
            scratchFbo = GpuFramebuffer.CreateEmpty("vge_bake_scratch");

            // Compile all programs using VGE's shader pipeline (imports, diagnostics, debug labels).
            // Each pass shares the same fullscreen vertex stage, but has its own fragment stage.
            // PbrHeightBakeShaderProgram automatically registers the shared params UBO binding.
            progLuminance = new PbrHeightBakeShaderProgram(FshLuminance, Domain);
            progGauss1D = new PbrHeightBakeShaderProgram(FshGauss1D, Domain);
            progSub = new PbrHeightBakeShaderProgram(FshSub, Domain);
            progCombine = new PbrHeightBakeShaderProgram(FshCombine, Domain);
            progGradient = new PbrHeightBakeShaderProgram(FshGradient, Domain);
            progDivergence = new PbrHeightBakeShaderProgram(FshDivergence, Domain);
            progJacobi = new PbrHeightBakeShaderProgram(FshJacobi, Domain);
            progResidual = new PbrHeightBakeShaderProgram(FshResidual, Domain);
            progRestrict = new PbrHeightBakeShaderProgram(FshRestrict, Domain);
            progProlongateAdd = new PbrHeightBakeShaderProgram(FshProlongateAdd, Domain);
            progNormalize = new PbrHeightBakeShaderProgram(FshNormalize, Domain);
            progPackToAtlas = new PbrHeightBakeShaderProgram(FshPackToAtlas, Domain);
            progCopy = new PbrHeightBakeShaderProgram(FshCopy, Domain);

            progLuminance!.Initialize(capi);
            progGauss1D!.Initialize(capi);
            progSub!.Initialize(capi);
            progCombine!.Initialize(capi);
            progGradient!.Initialize(capi);
            progDivergence!.Initialize(capi);
            progJacobi!.Initialize(capi);
            progResidual!.Initialize(capi);
            progRestrict!.Initialize(capi);
            progProlongateAdd!.Initialize(capi);
            progNormalize!.Initialize(capi);
            progPackToAtlas!.Initialize(capi);
            progCopy!.Initialize(capi);

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
        catch
        {
            // Failed preparation publishes no usable owner. Retire the entire candidate set before retry.
            PbrHeightBakeShaderProgram?[] candidates = [progLuminance, progGauss1D, progSub, progCombine,
                progGradient, progDivergence, progJacobi, progResidual, progRestrict, progProlongateAdd,
                progNormalize, progPackToAtlas, progCopy];
            foreach (var candidate in candidates) candidate?.Dispose();
            progLuminance = progGauss1D = progSub = progCombine = progGradient = progDivergence = null;
            progJacobi = progResidual = progRestrict = progProlongateAdd = progNormalize = progPackToAtlas = progCopy = null;
            geometry?.Dispose(); geometry = null;
            scratchFbo?.Dispose(); scratchFbo = null;
            throw;
        }
    }

    /// <summary>Prepares a solver executable and rejects unavailable shader assets.</summary>
    private static void CompileOrThrow(PbrHeightBakeShaderProgram program)
    {
        if (!program.CompileAndLink())
        {
            throw new InvalidOperationException($"[VGE] Failed to compile/link bake shader program '{program.PassName}'");
        }
    }

    /// <summary>Converts normalized atlas bounds into a nonempty clamped pixel rectangle.</summary>
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

    /// <summary>Clamps a value to the inclusive supported range.</summary>
    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

}
