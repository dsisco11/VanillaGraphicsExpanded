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

/// <summary>Owns the atlas baker Passes implementation.</summary>
internal static partial class MaterialAtlasNormalDepthGpuBuilder
{
    #region Private
    /// <summary>Extracts linear luminance from one source atlas rectangle.</summary>
    private static void RunLuminancePass(BakeDrawContext draw, int atlasTexId, (int x, int y, int w, int h) atlasRectPx, DynamicTexture2D dst)
    {
        draw.SetTarget(dst);
        progLuminance!.atlas = atlasTexId;

        // Pack all pass inputs before activation.
        {
            Params.LuminanceAtlasRect = atlasRectPx;
            Params.LuminanceDstSize = (dst.Width, dst.Height);
        }

        progLuminance!.Parameters = Params;
        draw.Draw(progLuminance!);

    }

    /// <summary>Publishes the bounded separable blur kernel to the shared parameter buffer.</summary>
    private static void WritePackedWeights65(float[] weights, int radius)
    {
        int r = Math.Clamp(radius, 0, MaxRadius);

        // Clamp weights to actual radius
        float[] clampedWeights = new float[r + 1];
        for (int i = 0; i <= r && i < weights.Length; i++)
        {
            clampedWeights[i] = weights[i];
        }

        Params.SetKernelWeights(clampedWeights, r + 1);
    }

    /// <summary>Runs horizontal and vertical blur passes using the same normalized kernel.</summary>
    private static void RunGaussian(BakeDrawContext draw, DynamicTexture2D src, DynamicTexture2D tmp, DynamicTexture2D dst, float sigma)
    {
        if (sigma <= 0.0001f)
        {
            RunCopy(draw, src, dst);
            return;
        }

        int radius = ComputeRadius(sigma);
        if (radius <= 0)
        {
            RunCopy(draw, src, dst);
            return;
        }

        var weights = BuildGaussianWeights(sigma, radius);

        // Horizontal
        draw.SetTarget(tmp);

        progGauss1D!.src = src;

        // Pack all pass inputs before activation.
        {
            Params.CommonSize = (src.Width, src.Height);
            Params.GaussianDirection = (1, 0);
            Params.GaussianParams = (radius, 0);
            WritePackedWeights65(weights, radius);
        }

        progGauss1D!.Parameters = Params;
        draw.Draw(progGauss1D!);


        // Vertical
        draw.SetTarget(dst);

        progGauss1D!.src = tmp;

        // Pack all pass inputs before activation.
        {
            Params.CommonSize = (dst.Width, dst.Height);
            Params.GaussianDirection = (0, 1);
            Params.GaussianParams = (radius, 0);
            WritePackedWeights65(weights, radius);
        }

        progGauss1D!.Parameters = Params;
        draw.Draw(progGauss1D!);

    }

    /// <summary>Subtracts a low-frequency field, optionally normalizing local contrast.</summary>
    private static void RunSub(BakeDrawContext draw, DynamicTexture2D a, DynamicTexture2D b, DynamicTexture2D dst, bool relContrast)
    {
        draw.SetTarget(dst);

        progSub!.a = a;
        progSub!.b = b;

        // Pack all pass inputs before activation.
        {
            Params.CommonSize = (dst.Width, dst.Height);
            Params.GaussianParams = (0, relContrast ? 1 : 0);
            Params.SubParams = (1e-6f, 8f);
        }

        progSub!.Parameters = Params;
        draw.Draw(progSub!);

    }

    /// <summary>Copies one solver image through a declared full-image pass.</summary>
    private static void RunCopy(BakeDrawContext draw, DynamicTexture2D src, DynamicTexture2D dst)
    {
        draw.SetTarget(dst);

        progCopy!.src = src;
        Params.CommonSize = (dst.Width, dst.Height);
        progCopy!.Parameters = Params;
        draw.Draw(progCopy!);

    }

    /// <summary>Combines four filtered bands into the requested weighted detail field.</summary>
    private static void RunCombine(BakeDrawContext draw, DynamicTexture2D g1, DynamicTexture2D g2, DynamicTexture2D g3, DynamicTexture2D g4, DynamicTexture2D dst, float w1, float w2, float w3)
    {
        draw.SetTarget(dst);

        progCombine!.g1 = g1;
        progCombine!.g2 = g2;
        progCombine!.g3 = g3;
        progCombine!.g4 = g4;

        // Pack all pass inputs before activation.
        {
            Params.CombineWeights = (w1, w2, w3);
            Params.CommonSize = (dst.Width, dst.Height);
        }
        progCombine!.Parameters = Params;
        draw.Draw(progCombine!);

    }

    /// <summary>Converts detail into a bounded gradient field using the configured edge suppression.</summary>
    private static void RunGradient(BakeDrawContext draw, DynamicTexture2D d, DynamicTexture2D dstG, float gain, float maxSlope, float edgeT0, float edgeT1)
    {
        draw.SetTarget(dstG);

        progGradient!.d = d;

        // Pack all pass inputs before activation.
        {
            Params.CommonSize = (d.Width, d.Height);
            Params.GradientParams = (gain, maxSlope, edgeT0, edgeT1);
        }
        progGradient!.Parameters = Params;
        draw.Draw(progGradient!);

    }

    /// <summary>Computes the divergence used as the Poisson right-hand side.</summary>
    private static void RunDivergence(BakeDrawContext draw, DynamicTexture2D g, DynamicTexture2D dstDiv)
    {
        draw.SetTarget(dstDiv);

        progDivergence!.g = g;
        Params.CommonSize = (dstDiv.Width, dstDiv.Height);
        progDivergence!.Parameters = Params;
        draw.Draw(progDivergence!);

    }

    /// <summary>Maps solved heights into the requested asymmetric normalized range.</summary>
    private static void RunNormalize(BakeDrawContext draw, DynamicTexture2D h, DynamicTexture2D dst, float mean, float invNeg, float invPos, float heightStrength, float gamma)
    {
        draw.SetTarget(dst);

        progNormalize!.h = h;

        // Pack all pass inputs before activation.
        {
            Params.CommonSize = (dst.Width, dst.Height);
            Params.NormalizeParams = (mean, invNeg, invPos, heightStrength);
            Params.NormalizeGamma = gamma;
        }
        progNormalize!.Parameters = Params;
        draw.Draw(progNormalize!);

    }

    /// <summary>Writes normals and normalized height only into the selected atlas rectangle.</summary>
    private static void RunPackToAtlas(BakeDrawContext draw,
        int dstAtlasTexId,
        (int x, int y) viewportOriginPx,
        (int w, int h) tileSizePx,
        (int w, int h) solverSizePx,
        DynamicTexture2D heightTex,
        int baseAlbedoAtlasTexId,
        float normalStrength,
        float normalScale,
        float depthScale)
    {
        // Render into atlas sidecar, restricting to this tile rect via viewport.
        draw.SetAtlasTarget(dstAtlasTexId, viewportOriginPx.x, viewportOriginPx.y, tileSizePx.w, tileSizePx.h);

        progPackToAtlas!.height = heightTex;
        progPackToAtlas!.albedoAtlas = baseAlbedoAtlasTexId;

        // Pack all pass inputs before activation.
        {
            Params.SolverSize = (solverSizePx.w, solverSizePx.h);
            Params.TileSize = (tileSizePx.w, tileSizePx.h);
            Params.ViewportOrigin = (viewportOriginPx.x, viewportOriginPx.y);
            Params.PackParams = (normalStrength, normalScale, depthScale, 0.001f);
        }
        progPackToAtlas!.Parameters = Params;
        draw.Draw(progPackToAtlas!);

    }

    /// <summary>Bounds the Gaussian kernel radius to the supported packed weight capacity.</summary>
    private static int ComputeRadius(float sigma)
    {
        int r = (int)Math.Ceiling(3.0 * sigma);
        if (r > MaxRadius) r = MaxRadius;
        return r;
    }

    /// <summary>Builds symmetric normalized weights for the separable Gaussian filter.</summary>
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

    #endregion
}
