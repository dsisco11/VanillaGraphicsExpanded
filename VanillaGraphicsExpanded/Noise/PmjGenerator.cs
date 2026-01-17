using System;
using System.Numerics;
using System.Numerics.Tensors;

namespace VanillaGraphicsExpanded.Noise;

public static class PmjGenerator
{
    public static PmjSequence Generate(in PmjConfig config)
    {
        config.Validate();

        return config.Variant switch
        {
            PmjVariant.Pmj02 => GeneratePmj02Like(config),
            _ => throw new NotSupportedException($"Unsupported {nameof(PmjVariant)}: {config.Variant}.")
        };
    }

    private static PmjSequence GeneratePmj02Like(in PmjConfig config)
    {
        // Implementation note:
        // We generate a progressive (0,2)-style 2D sequence using Sobol base-2 construction
        // and optional Owen-style scrambling driven by Squirrel3 hashing.
        // This provides the required properties for temporal jitter: good progressive prefixes,
        // deterministic seeding, and optional decorrelation via salt/scramble.

        int n = config.SampleCount;

        // Generate raw 32-bit fixed-point samples.
        var xBits = new uint[n];
        var yBits = new uint[n];

        PmjSobol02.FillBitsProgressive(n, xBits, yBits);

        uint scrambleSeedX = Squirrel3Noise.HashU(config.Seed, 0xA53C_9E71u, config.Salt);
        uint scrambleSeedY = Squirrel3Noise.HashU(config.Seed, 0xB7E1_51A5u, config.Salt);

        if (config.OwenScramble)
        {
            for (int i = 0; i < n; i++)
            {
                xBits[i] = PmjOwenScrambler.ScrambleBits(xBits[i], scrambleSeedX);
                yBits[i] = PmjOwenScrambler.ScrambleBits(yBits[i], scrambleSeedY);
            }
        }

        // Convert to [0,1) floats using TensorPrimitives for bulk operations.
        float[] xf = new float[n];
        float[] yf = new float[n];
        PmjSobol02.ToUnitFloat01(xBits, xf);
        PmjSobol02.ToUnitFloat01(yBits, yf);

        if (config.Centered || config.OutputKind == PmjOutputKind.Vector2F32Centered)
        {
            TensorPrimitives.Add(xf, -0.5f, xf);
            TensorPrimitives.Add(yf, -0.5f, yf);
        }

        var points = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            points[i] = new Vector2(xf[i], yf[i]);
        }

        return new PmjSequence(n, points);
    }
}
