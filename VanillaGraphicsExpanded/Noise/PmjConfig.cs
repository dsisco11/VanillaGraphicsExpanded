using System;

namespace VanillaGraphicsExpanded.Noise;

public readonly record struct PmjConfig(
    int SampleCount,
    uint Seed = 0,
    PmjVariant Variant = PmjVariant.Pmj02,
    PmjOutputKind OutputKind = PmjOutputKind.Vector2F32,
    bool OwenScramble = false,
    uint Salt = 0,
    bool Centered = false)
{
    public void Validate()
    {
        if (!TryValidate(out string? error))
        {
            throw new ArgumentOutOfRangeException(nameof(PmjConfig), error);
        }
    }

    public bool TryValidate(out string? error)
    {
        if (SampleCount <= 0)
        {
            error = $"{nameof(SampleCount)} must be > 0 (was {SampleCount}).";
            return false;
        }

        if (!Enum.IsDefined(typeof(PmjVariant), Variant))
        {
            error = $"{nameof(Variant)} is not a known value ({Variant}).";
            return false;
        }

        if (!Enum.IsDefined(typeof(PmjOutputKind), OutputKind))
        {
            error = $"{nameof(OutputKind)} is not a known value ({OutputKind}).";
            return false;
        }

        // Centering is a semantic mapping; keep both knobs but ensure they don't conflict.
        if (Centered && OutputKind is not PmjOutputKind.Vector2F32Centered)
        {
            // Allow Centered even when output kind isn't explicitly "Centered" yet.
            // The generator will treat this as a request for centered mapping when returning float2.
        }

        // Seed/Salt are uint; always valid.
        error = null;
        return true;
    }
}
