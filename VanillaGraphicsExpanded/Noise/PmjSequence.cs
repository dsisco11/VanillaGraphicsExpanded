using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.Noise;

public sealed class PmjSequence
{
    private readonly Vector2[] _points;

    public PmjSequence(int count, Vector2[] points)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be > 0.");
        _points = points ?? throw new ArgumentNullException(nameof(points));

        if (_points.Length != count)
        {
            throw new ArgumentException($"Length mismatch (expected {count}, points={_points.Length}).", nameof(points));
        }

        Count = count;
    }

    public int Count { get; }

    /// <summary>
    /// Canonical PMJ sequence points in [0, 1)^2.
    /// </summary>
    public ReadOnlyMemory<Vector2> Points => _points;

    public ReadOnlySpan<Vector2> PointsSpan => _points;
}
