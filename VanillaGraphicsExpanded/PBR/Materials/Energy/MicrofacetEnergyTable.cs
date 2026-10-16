using System;
using System.IO;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Materials.Energy;

/// <summary>Decodes the immutable directional-albedo asset used by GPU receivers and diffuse cache material adapters.</summary>
internal sealed class MicrofacetEnergyTable
{
    internal const int Size = 128;
    internal const float MinimumRoughness = .04f;
    private readonly float[] samples;
    private static readonly Lazy<MicrofacetEnergyTable> shared = new(LoadEmbedded);

    #region Public API
    /// <summary>Loads and validates the packaged table once without touching GPU state.</summary>
    internal static MicrofacetEnergyTable Shared => shared.Value;
    /// <summary>Exposes immutable interleaved E/Ef payload for one GPU upload.</summary>
    internal ReadOnlySpan<float> Samples => samples;

    /// <summary>Validates representation and physical energy intervals before publishing any decoded samples.</summary>
    internal MicrofacetEnergyTable(Stream source)
    {
        using var reader = new BinaryReader(source, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != 0x45475856u || reader.ReadInt32() != 1 || reader.ReadInt32() != Size
            || reader.ReadInt32() < 16384 || reader.ReadSingle() != MinimumRoughness
            || reader.ReadInt32() != 2 || reader.ReadInt32() != 4 || reader.ReadInt32() != 2)
            throw new InvalidDataException("Unsupported microfacet energy table contract.");
        var candidate = new float[Size * Size * 2];
        // Validate the complete candidate before it becomes available to either CPU or GPU consumers.
        for (int i = 0; i < candidate.Length; i += 2)
        {
            float e = reader.ReadSingle(), ef = reader.ReadSingle();
            if (!float.IsFinite(e) || !float.IsFinite(ef) || e < .25f || e > 1 || ef < 0 || ef > e)
                throw new InvalidDataException("Invalid microfacet energy sample.");
            candidate[i] = e; candidate[i + 1] = ef;
        }
        if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("Unexpected microfacet energy payload.");
        samples = candidate;
    }

    /// <summary>Matches shader bilinear interpolation on the quadratic roughness and quartic view axes.</summary>
    internal Vector2 Sample(float roughness, float nDotV)
    {
        if (!float.IsFinite(roughness) || !float.IsFinite(nDotV)) throw new ArgumentOutOfRangeException(nameof(roughness));
        float x = MathF.Sqrt((Math.Clamp(roughness, MinimumRoughness, 1) - MinimumRoughness) / .96f) * (Size - 1);
        float y = MathF.Sqrt(MathF.Sqrt(Math.Clamp(nDotV, 0, 1))) * (Size - 1);
        int ix = Math.Min((int)x, Size - 2), iy = Math.Min((int)y, Size - 2);
        Vector2 lower = Vector2.Lerp(Node(ix, iy), Node(ix + 1, iy), x - ix);
        Vector2 upper = Vector2.Lerp(Node(ix, iy + 1), Node(ix + 1, iy + 1), x - ix);
        return Vector2.Lerp(lower, upper, y - iy);
    }

    /// <summary>Resamples the validated source on unchanged warped axes and rounds each node to RG16F storage precision.</summary>
    internal float[] CreateTextureData(int size)
    {
        if (size is not (16 or 32 or 64 or 128)) throw new ArgumentOutOfRangeException(nameof(size));
        var pixels = new float[size * size * 2];
        if (size == Size)
        {
            // The full-resolution asset already occupies these nodes. Avoid a coordinate
            // round trip that could change which side of a half-precision midpoint is chosen.
            for (int i = 0; i < samples.Length; i++) pixels[i] = (float)(Half)samples[i];
            return pixels;
        }
        // Quality changes sampling density, not material calibration. Half rounding here makes
        // the published payload and its CPU validation independent of driver conversion details.
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (float)x / (size - 1), v = (float)y / (size - 1);
            Vector2 value = Sample(MinimumRoughness + .96f * u * u, v * v * v * v);
            int offset = (y * size + x) * 2;
            pixels[offset] = (float)(Half)value.X;
            pixels[offset + 1] = (float)(Half)value.Y;
        }
        return pixels;
    }

    /// <summary>Reserves the compensated specular fraction when encoding a view-independent Lambertian cache material.</summary>
    internal Vector3 DiffuseAlbedo(PbrMaterialSurface material)
    {
        Vector3 integrated = Vector3.Zero;
        Vector3 f0 = Vector3.Clamp(material.SpecularF0, Vector3.Zero, Vector3.One);
        Vector3 previous = Reflectance(Sample(material.Roughness, 0), f0);
        float previousMu = 0;
        // Cache outgoing radiance is diffuse-only. Average its interface budget over projected view directions,
        // rather than baking a view-dependent specular lobe or compensating already reflected cache lighting.
        for (int y = 1; y < Size; y++)
        {
            float mu = MathF.Pow((float)y / (Size - 1), 4);
            Vector3 current = Reflectance(Sample(material.Roughness, mu), f0);
            integrated += (previous * previousMu + current * mu) * (mu - previousMu);
            previous = current; previousMu = mu;
        }
        return material.DiffuseAlbedo * (Vector3.One - Vector3.Clamp(integrated, Vector3.Zero, Vector3.One));
    }

    /// <summary>Evaluates compensated lobe reflectance with the material model's unit grazing reflectance.</summary>
    internal static Vector3 Reflectance(Vector2 energy, Vector3 f0)
    {
        Vector3 weight = Vector3.One + f0 * ((1 - energy.X) / energy.X);
        return Vector3.Clamp(weight * (energy.X * f0 + energy.Y * (Vector3.One - f0)), Vector3.Zero, Vector3.One);
    }
    #endregion

    #region Private
    /// <summary>Returns a node in row-major material/view order.</summary>
    private Vector2 Node(int x, int y)
    {
        int index = 2 * (y * Size + x);
        return new(samples[index], samples[index + 1]);
    }

    /// <summary>Obtains the model-versioned asset embedded in the tested production assembly.</summary>
    private static MicrofacetEnergyTable LoadEmbedded()
    {
        using var source = typeof(MicrofacetEnergyTable).Assembly.GetManifestResourceStream("VGE.MicrofacetEnergy")
            ?? throw new InvalidDataException("The microfacet energy asset is missing.");
        return new(source);
    }
    #endregion
}
