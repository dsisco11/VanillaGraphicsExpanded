using System;

namespace VanillaGraphicsExpanded.Noise;

public readonly record struct BlueNoiseConfig(
    int Width,
    int Height,
    int Slices = 1,
    bool Tileable = true,
    uint Seed = 0,
    BlueNoiseAlgorithm Algorithm = BlueNoiseAlgorithm.VoidAndCluster,
    BlueNoiseOutputKind OutputKind = BlueNoiseOutputKind.RankU16,
    float Sigma = 1.5f,
    float InitialFillRatio = 0.1f,
    int MaxIterations = 0,
    int StagnationLimit = 0)
{
    public int PixelCount => checked(Width * Height);

    public void Validate()
    {
        if (!TryValidate(out string? error))
        {
            throw new ArgumentOutOfRangeException(nameof(BlueNoiseConfig), error);
        }
    }

    public bool TryValidate(out string? error)
    {
        if (Width <= 0)
        {
            error = $"{nameof(Width)} must be > 0 (was {Width}).";
            return false;
        }

        if (Height <= 0)
        {
            error = $"{nameof(Height)} must be > 0 (was {Height}).";
            return false;
        }

        if (Slices <= 0)
        {
            error = $"{nameof(Slices)} must be > 0 (was {Slices}).";
            return false;
        }

        if (!float.IsFinite(Sigma) || Sigma <= 0)
        {
            error = $"{nameof(Sigma)} must be finite and > 0 (was {Sigma}).";
            return false;
        }

        if (!float.IsFinite(InitialFillRatio) || InitialFillRatio <= 0 || InitialFillRatio >= 1)
        {
            error = $"{nameof(InitialFillRatio)} must be finite and in (0, 1) (was {InitialFillRatio}).";
            return false;
        }

        if (MaxIterations < 0)
        {
            error = $"{nameof(MaxIterations)} must be >= 0 (was {MaxIterations}).";
            return false;
        }

        if (StagnationLimit < 0)
        {
            error = $"{nameof(StagnationLimit)} must be >= 0 (was {StagnationLimit}).";
            return false;
        }

        if (MaxIterations > 0 && StagnationLimit > MaxIterations)
        {
            error = $"{nameof(StagnationLimit)} must be <= {nameof(MaxIterations)} when a max is specified (stagnation={StagnationLimit}, max={MaxIterations}).";
            return false;
        }

        // Ensure PixelCount doesn't overflow.
        _ = PixelCount;

        error = null;
        return true;
    }
}
