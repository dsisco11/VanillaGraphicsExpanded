using System;

namespace VanillaGraphicsExpanded.Noise;

public sealed class BlueNoiseRankMap
{
    private readonly ushort[] _ranks;

    public BlueNoiseRankMap(int width, int height, ushort[] ranks)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be > 0.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be > 0.");
        _ranks = ranks ?? throw new ArgumentNullException(nameof(ranks));

        int expectedLength = checked(width * height);
        if (_ranks.Length != expectedLength)
        {
            throw new ArgumentException($"Length mismatch (expected {expectedLength}, ranks={_ranks.Length}).", nameof(ranks));
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public int Length => _ranks.Length;

    public ReadOnlyMemory<ushort> Ranks => _ranks;

    public ReadOnlySpan<ushort> RanksSpan => _ranks;
}
