using System;
using System.Collections.Generic;
using System.Linq;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;

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
    private static int fbo;

    private static int progLuminance;
    private static int progGauss1D;
    private static int progSub;
    private static int progCombine;
    private static int progGradient;
    private static int progDivergence;
    private static int progJacobi;
    private static int progResidual;
    private static int progRestrict;
    private static int progProlongateAdd;
    private static int progNormalize;
    private static int progPackToAtlas;
    private static int progCopy;

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
        GL.GetInteger(GetPName.FramebufferBinding, out int prevFbo);
        GL.GetInteger(GetPName.VertexArrayBinding, out int prevVao);
        GL.GetInteger(GetPName.CurrentProgram, out int prevProgram);
        GL.GetInteger(GetPName.ActiveTexture, out int prevActiveTex);
        GL.GetInteger(GetPName.TextureBinding2D, out int prevTex2D);

        try
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            GL.BindVertexArray(vao);

            // Clear entire atlas sidecar first (deterministic baseline).
            AttachAndViewport(destNormalDepthTexId, 0, 0, atlasWidth, atlasHeight);
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
                    heightTexId: tile.Hn.TextureId,
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
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
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
        fbo = GL.GenFramebuffer();

        // Compile all programs from assets.
        progLuminance = CompileProgramFromAssets(capi, Vsh, FshLuminance);
        progGauss1D = CompileProgramFromAssets(capi, Vsh, FshGauss1D);
        progSub = CompileProgramFromAssets(capi, Vsh, FshSub);
        progCombine = CompileProgramFromAssets(capi, Vsh, FshCombine);
        progGradient = CompileProgramFromAssets(capi, Vsh, FshGradient);
        progDivergence = CompileProgramFromAssets(capi, Vsh, FshDivergence);
        progJacobi = CompileProgramFromAssets(capi, Vsh, FshJacobi);
        progResidual = CompileProgramFromAssets(capi, Vsh, FshResidual);
        progRestrict = CompileProgramFromAssets(capi, Vsh, FshRestrict);
        progProlongateAdd = CompileProgramFromAssets(capi, Vsh, FshProlongateAdd);
        progNormalize = CompileProgramFromAssets(capi, Vsh, FshNormalize);
        progPackToAtlas = CompileProgramFromAssets(capi, Vsh, FshPackToAtlas);
        progCopy = CompileProgramFromAssets(capi, Vsh, FshCopy);

        initialized = true;
    }

    private static int CompileProgramFromAssets(ICoreClientAPI capi, string vshName, string fshName)
    {
        string vsh = LoadTextAsset(capi, $"shaders/{vshName}.vsh");
        string fsh = LoadTextAsset(capi, $"shaders/{fshName}.fsh");

        int vs = CompileShader(ShaderType.VertexShader, vsh, vshName);
        int fs = CompileShader(ShaderType.FragmentShader, fsh, fshName);

        int prog = GL.CreateProgram();
        GL.AttachShader(prog, vs);
        GL.AttachShader(prog, fs);
        GL.LinkProgram(prog);
        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
        string log = GL.GetProgramInfoLog(prog) ?? string.Empty;
        GL.DetachShader(prog, vs);
        GL.DetachShader(prog, fs);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);

        if (ok == 0)
        {
            GL.DeleteProgram(prog);
            throw new InvalidOperationException($"[VGE] Failed to link bake program '{vshName}+{fshName}':\n{log}");
        }

        return prog;
    }

    private static int CompileShader(ShaderType type, string src, string name)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, src);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
        string log = GL.GetShaderInfoLog(shader) ?? string.Empty;
        if (ok == 0)
        {
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"[VGE] Failed to compile bake shader '{name}' ({type}):\n{log}");
        }
        return shader;
    }

    private static string LoadTextAsset(ICoreClientAPI capi, string path)
    {
        var asset = capi.Assets.TryGet(Vintagestory.API.Common.AssetLocation.Create(path, Domain), loadAsset: true);
        if (asset is null)
        {
            throw new InvalidOperationException($"[VGE] Missing bake shader asset: {Domain}:{path}");
        }

        // IAsset exposes ToText(); avoid encoding issues.
        return asset.ToText();
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

    private static void AttachAndViewport(int texId, int x, int y, int w, int h)
    {
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texId, 0);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GL.Viewport(x, y, w, h);
    }

    private static void DrawFullscreenTriangle()
    {
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private static void BindTex(int unit, int texId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, texId);
    }

    private static void RunLuminancePass(int atlasTexId, (int x, int y, int w, int h) atlasRectPx, DynamicTexture dst)
    {
        AttachAndViewport(dst.TextureId, 0, 0, dst.Width, dst.Height);

        GL.UseProgram(progLuminance);
        BindTex(0, atlasTexId);

        GL.Uniform1(GL.GetUniformLocation(progLuminance, "u_atlas"), 0);
        GL.Uniform4(GL.GetUniformLocation(progLuminance, "u_atlasRectPx"), atlasRectPx.x, atlasRectPx.y, atlasRectPx.w, atlasRectPx.h);
        GL.Uniform2(GL.GetUniformLocation(progLuminance, "u_outSize"), dst.Width, dst.Height);

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
        AttachAndViewport(tmp.TextureId, 0, 0, tmp.Width, tmp.Height);
        GL.UseProgram(progGauss1D);
        BindTex(0, src.TextureId);
        GL.Uniform1(GL.GetUniformLocation(progGauss1D, "u_src"), 0);
        GL.Uniform2(GL.GetUniformLocation(progGauss1D, "u_size"), src.Width, src.Height);
        GL.Uniform2(GL.GetUniformLocation(progGauss1D, "u_dir"), 1, 0);
        GL.Uniform1(GL.GetUniformLocation(progGauss1D, "u_radius"), radius);
        UploadWeights(progGauss1D, weights);
        DrawFullscreenTriangle();

        // Vertical
        AttachAndViewport(dst.TextureId, 0, 0, dst.Width, dst.Height);
        GL.UseProgram(progGauss1D);
        BindTex(0, tmp.TextureId);
        GL.Uniform1(GL.GetUniformLocation(progGauss1D, "u_src"), 0);
        GL.Uniform2(GL.GetUniformLocation(progGauss1D, "u_size"), dst.Width, dst.Height);
        GL.Uniform2(GL.GetUniformLocation(progGauss1D, "u_dir"), 0, 1);
        GL.Uniform1(GL.GetUniformLocation(progGauss1D, "u_radius"), radius);
        UploadWeights(progGauss1D, weights);
        DrawFullscreenTriangle();
    }

    private static void RunSub(DynamicTexture a, DynamicTexture b, DynamicTexture dst)
    {
        AttachAndViewport(dst.TextureId, 0, 0, dst.Width, dst.Height);
        GL.UseProgram(progSub);
        BindTex(0, a.TextureId);
        BindTex(1, b.TextureId);
        GL.Uniform1(GL.GetUniformLocation(progSub, "u_a"), 0);
        GL.Uniform1(GL.GetUniformLocation(progSub, "u_b"), 1);
        GL.Uniform2(GL.GetUniformLocation(progSub, "u_size"), dst.Width, dst.Height);
        DrawFullscreenTriangle();
    }

    private static void RunCopy(DynamicTexture src, DynamicTexture dst)
    {
        AttachAndViewport(dst.TextureId, 0, 0, dst.Width, dst.Height);
        GL.UseProgram(progCopy);
        BindTex(0, src.TextureId);
        GL.Uniform1(GL.GetUniformLocation(progCopy, "u_src"), 0);
        GL.Uniform2(GL.GetUniformLocation(progCopy, "u_size"), dst.Width, dst.Height);
        DrawFullscreenTriangle();
    }

    private static void RunCombine(DynamicTexture g1, DynamicTexture g2, DynamicTexture g3, DynamicTexture g4, DynamicTexture dst, float w1, float w2, float w3)
    {
        AttachAndViewport(dst.TextureId, 0, 0, dst.Width, dst.Height);
        GL.UseProgram(progCombine);
        BindTex(0, g1.TextureId);
        BindTex(1, g2.TextureId);
        BindTex(2, g3.TextureId);
        BindTex(3, g4.TextureId);
        GL.Uniform1(GL.GetUniformLocation(progCombine, "u_g1"), 0);
        GL.Uniform1(GL.GetUniformLocation(progCombine, "u_g2"), 1);
        GL.Uniform1(GL.GetUniformLocation(progCombine, "u_g3"), 2);
        GL.Uniform1(GL.GetUniformLocation(progCombine, "u_g4"), 3);
        GL.Uniform3(GL.GetUniformLocation(progCombine, "u_w"), w1, w2, w3);
        GL.Uniform2(GL.GetUniformLocation(progCombine, "u_size"), dst.Width, dst.Height);
        DrawFullscreenTriangle();
    }

    private static void RunGradient(DynamicTexture d, DynamicTexture dstG, LumOnConfig.NormalDepthBakeConfig bake)
    {
        AttachAndViewport(dstG.TextureId, 0, 0, dstG.Width, dstG.Height);
        GL.UseProgram(progGradient);
        BindTex(0, d.TextureId);
        GL.Uniform1(GL.GetUniformLocation(progGradient, "u_d"), 0);
        GL.Uniform2(GL.GetUniformLocation(progGradient, "u_size"), d.Width, d.Height);
        GL.Uniform1(GL.GetUniformLocation(progGradient, "u_gain"), bake.Gain);
        GL.Uniform1(GL.GetUniformLocation(progGradient, "u_maxSlope"), bake.MaxSlope);
        GL.Uniform2(GL.GetUniformLocation(progGradient, "u_edgeT"), bake.EdgeT0, bake.EdgeT1);
        DrawFullscreenTriangle();
    }

    private static void RunDivergence(DynamicTexture g, DynamicTexture dstDiv)
    {
        AttachAndViewport(dstDiv.TextureId, 0, 0, dstDiv.Width, dstDiv.Height);
        GL.UseProgram(progDivergence);
        BindTex(0, g.TextureId);
        GL.Uniform1(GL.GetUniformLocation(progDivergence, "u_g"), 0);
        GL.Uniform2(GL.GetUniformLocation(progDivergence, "u_size"), dstDiv.Width, dstDiv.Height);
        DrawFullscreenTriangle();
    }

    private static void RunNormalize(DynamicTexture h, DynamicTexture dst, float mean, float heightStrength, float gamma)
    {
        AttachAndViewport(dst.TextureId, 0, 0, dst.Width, dst.Height);
        GL.UseProgram(progNormalize);
        BindTex(0, h.TextureId);
        GL.Uniform1(GL.GetUniformLocation(progNormalize, "u_h"), 0);
        GL.Uniform2(GL.GetUniformLocation(progNormalize, "u_size"), dst.Width, dst.Height);
        GL.Uniform1(GL.GetUniformLocation(progNormalize, "u_mean"), mean);
        GL.Uniform1(GL.GetUniformLocation(progNormalize, "u_heightStrength"), heightStrength);
        GL.Uniform1(GL.GetUniformLocation(progNormalize, "u_gamma"), gamma);
        DrawFullscreenTriangle();
    }

    private static void RunPackToAtlas(
        int dstAtlasTexId,
        (int x, int y) viewportOriginPx,
        (int w, int h) tileSizePx,
        (int w, int h) solverSizePx,
        int heightTexId,
        float normalStrength)
    {
        // Render into atlas sidecar, restricting to this tile rect via viewport.
        AttachAndViewport(dstAtlasTexId, viewportOriginPx.x, viewportOriginPx.y, tileSizePx.w, tileSizePx.h);
        GL.UseProgram(progPackToAtlas);
        BindTex(0, heightTexId);
        GL.Uniform1(GL.GetUniformLocation(progPackToAtlas, "u_height"), 0);
        GL.Uniform2(GL.GetUniformLocation(progPackToAtlas, "u_solverSize"), solverSizePx.w, solverSizePx.h);
        GL.Uniform2(GL.GetUniformLocation(progPackToAtlas, "u_tileSize"), tileSizePx.w, tileSizePx.h);
        GL.Uniform2(GL.GetUniformLocation(progPackToAtlas, "u_viewportOrigin"), viewportOriginPx.x, viewportOriginPx.y);
        GL.Uniform1(GL.GetUniformLocation(progPackToAtlas, "u_normalStrength"), normalStrength);
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

    private static void UploadWeights(int program, float[] weights)
    {
        int loc = GL.GetUniformLocation(program, "u_weights");
        if (loc < 0) return;
        GL.Uniform1(loc, weights.Length, weights);
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
            AttachAndViewport(tex.TextureId, 0, 0, tex.Width, tex.Height);
            GL.ClearColor(v, 0f, 0f, 0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        // Uses outer RunCopy

        private static void RunJacobi(DynamicTexture h, DynamicTexture b, DynamicTexture dst)
        {
            AttachAndViewport(dst.TextureId, 0, 0, dst.Width, dst.Height);
            GL.UseProgram(progJacobi);
            BindTex(0, h.TextureId);
            BindTex(1, b.TextureId);
            GL.Uniform1(GL.GetUniformLocation(progJacobi, "u_h"), 0);
            GL.Uniform1(GL.GetUniformLocation(progJacobi, "u_b"), 1);
            GL.Uniform2(GL.GetUniformLocation(progJacobi, "u_size"), dst.Width, dst.Height);
            DrawFullscreenTriangle();
        }

        private static void RunResidual(DynamicTexture h, DynamicTexture b, DynamicTexture dst)
        {
            AttachAndViewport(dst.TextureId, 0, 0, dst.Width, dst.Height);
            GL.UseProgram(progResidual);
            BindTex(0, h.TextureId);
            BindTex(1, b.TextureId);
            GL.Uniform1(GL.GetUniformLocation(progResidual, "u_h"), 0);
            GL.Uniform1(GL.GetUniformLocation(progResidual, "u_b"), 1);
            GL.Uniform2(GL.GetUniformLocation(progResidual, "u_size"), dst.Width, dst.Height);
            DrawFullscreenTriangle();
        }

        private static void RunRestrict(DynamicTexture fine, DynamicTexture coarse)
        {
            AttachAndViewport(coarse.TextureId, 0, 0, coarse.Width, coarse.Height);
            GL.UseProgram(progRestrict);
            BindTex(0, fine.TextureId);
            GL.Uniform1(GL.GetUniformLocation(progRestrict, "u_fine"), 0);
            GL.Uniform2(GL.GetUniformLocation(progRestrict, "u_fineSize"), fine.Width, fine.Height);
            GL.Uniform2(GL.GetUniformLocation(progRestrict, "u_coarseSize"), coarse.Width, coarse.Height);
            DrawFullscreenTriangle();
        }

        private static void RunProlongateAdd(DynamicTexture fineH, DynamicTexture coarseE, DynamicTexture dst)
        {
            AttachAndViewport(dst.TextureId, 0, 0, dst.Width, dst.Height);
            GL.UseProgram(progProlongateAdd);
            BindTex(0, fineH.TextureId);
            BindTex(1, coarseE.TextureId);
            GL.Uniform1(GL.GetUniformLocation(progProlongateAdd, "u_fineH"), 0);
            GL.Uniform1(GL.GetUniformLocation(progProlongateAdd, "u_coarseE"), 1);
            GL.Uniform2(GL.GetUniformLocation(progProlongateAdd, "u_fineSize"), fineH.Width, fineH.Height);
            GL.Uniform2(GL.GetUniformLocation(progProlongateAdd, "u_coarseSize"), coarseE.Width, coarseE.Height);
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
