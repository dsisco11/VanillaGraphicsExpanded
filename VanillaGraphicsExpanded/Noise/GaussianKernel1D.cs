using System;

namespace VanillaGraphicsExpanded.Noise;

public readonly record struct GaussianKernel1D
{
    public GaussianKernel1D(float sigma, int radius, float[] weights)
    {
        if (!float.IsFinite(sigma) || sigma <= 0) throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "Sigma must be finite and > 0.");
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be >= 0.");
        Weights = weights ?? throw new ArgumentNullException(nameof(weights));

        int expected = checked((radius * 2) + 1);
        if (Weights.Length != expected)
        {
            throw new ArgumentException($"Kernel length mismatch (expected {expected}, weights={Weights.Length}).", nameof(weights));
        }

        Sigma = sigma;
        Radius = radius;
    }

    public float Sigma { get; }

    public int Radius { get; }

    public ReadOnlyMemory<float> Weights { get; }

    public static GaussianKernel1D Create(float sigma)
    {
        if (!float.IsFinite(sigma) || sigma <= 0) throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "Sigma must be finite and > 0.");

        // Radius rule: ceil(3*sigma) is a common practical cutoff for Gaussian tails.
        int radius = Math.Max(1, (int)MathF.Ceiling(3f * sigma));
        int size = checked((radius * 2) + 1);

        var weights = new float[size];

        float invTwoSigma2 = 1f / (2f * sigma * sigma);

        float sum = 0f;
        for (int i = -radius; i <= radius; i++)
        {
            float w = MathF.Exp(-(i * i) * invTwoSigma2);
            weights[i + radius] = w;
            sum += w;
        }

        float invSum = 1f / sum;
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] *= invSum;
        }

        return new GaussianKernel1D(sigma, radius, weights);
    }
}
