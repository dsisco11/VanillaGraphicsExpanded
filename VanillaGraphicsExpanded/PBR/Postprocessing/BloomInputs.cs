using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Postprocessing;

/// <summary>Publishes bloom filtering and source-local exposure policy in six std140 vector slots.</summary>
internal sealed class BloomInputs : CpuUniformBuffer
{
    private static readonly GaussianKernel[] kernels = CreateGaussianKernels();
    #region Public API
    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 96;
    internal const int PassOffset = 0;
    internal const int EffectOffset = 16;
    internal const int WeightsOffset = 32;
    internal const int ReservedOffset = 48;
    internal const int LocalLimitsOffset = 64;
    internal const int LocalPolicyOffset = 80;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Allocates filtering parameters followed by optional source exposure controls.</summary>
    internal BloomInputs() : base() { }

    #region Filtering operations
    /// <summary>Stages threshold extraction or subsequent pyramid reduction.</summary>
    internal void CaptureExtraction(bool extract, bool automaticExposure, float threshold, float knee, float strength, float manualLogGain)
    {
        WriteVector4(PassOffset, new(0, 0, extract ? 0 : 1, automaticExposure ? 1 : 0));
        WriteVector4(EffectOffset, new(threshold, knee, strength, manualLogGain));
        WriteVector4(WeightsOffset, Vector4.Zero);
        WriteVector4(ReservedOffset, Vector4.Zero);
    }

    /// <summary>Stages paired linear Gaussian samples without changing source exposure policy.</summary>
    internal void CaptureGaussian(int radius, bool vertical)
    {
        if (radius < 1 || radius > 8) throw new ArgumentOutOfRangeException(nameof(radius));
        var kernel = kernels[radius];
        WriteVector4(PassOffset, new(radius, kernel.Center, vertical ? 3 : 2, 0));
        WriteVector4(EffectOffset, kernel.Weights);
        WriteVector4(WeightsOffset, kernel.Offsets);
        WriteVector4(ReservedOffset, Vector4.Zero);
    }

    /// <summary>Stages one scale's weighted tint and optional coarser contribution.</summary>
    internal void CaptureAccumulation(Vector3 tint, bool hasCoarser)
    {
        WriteVector4(PassOffset, new(0, 0, 4, hasCoarser ? 1 : 0));
        WriteVector4(EffectOffset, new(tint, 0));
        WriteVector4(WeightsOffset, Vector4.Zero);
        WriteVector4(ReservedOffset, Vector4.Zero);
    }

    #endregion

    #region Local exposure
    /// <summary>Captures the same bounded correction and middle-gray pivot used by the scene display.</summary>
    internal void CaptureLocal(LocalExposureParameters settings, float logMiddleGray)
    {
        WriteVector4(LocalLimitsOffset, new(settings.ShadowStrength, settings.HighlightStrength, settings.ShadowLimit, settings.HighlightLimit));
        WriteVector4(LocalPolicyOffset, new(settings.RangeWidth, settings.Enabled ? 1 : 0, logMiddleGray, 0));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Stores the center coefficient and four symmetric bilinear tap pairs.</summary>
    private readonly record struct GaussianKernel(float Center, Vector4 Weights, Vector4 Offsets);

    /// <summary>Precomputes normalized, contiguous Gaussian coefficients and combines adjacent samples exactly.</summary>
    private static GaussianKernel[] CreateGaussianKernels()
    {
        var result = new GaussianKernel[9];
        for (int radius = 1; radius <= 8; radius++)
        {
            var coefficients = new double[radius + 1];
            double sum = 0;
            for (int tap = 0; tap <= radius; tap++)
            {
                // The support reaches negligible tail energy rather than cutting a visible Gaussian shoulder.
                coefficients[tap] = Math.Exp(-16.7 * tap * tap / (radius * radius));
                sum += coefficients[tap] * (tap == 0 ? 1 : 2);
            }
            var weights = new float[4];
            var offsets = new float[4];
            for (int pair = 0; pair < 4; pair++)
            {
                int first = pair * 2 + 1;
                if (first > radius) break;
                double a = coefficients[first], b = first < radius ? coefficients[first + 1] : 0;
                // Linear interpolation at this offset reproduces both adjacent weighted texels.
                weights[pair] = (float)((a + b) / sum);
                offsets[pair] = first + (float)(b / (a + b));
            }
            result[radius] = new((float)(coefficients[0] / sum),
                new(weights[0], weights[1], weights[2], weights[3]),
                new(offsets[0], offsets[1], offsets[2], offsets[3]));
        }
        return result;
    }
    #endregion
}
