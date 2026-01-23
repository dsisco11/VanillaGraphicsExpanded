using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal static class WorldProbeLightSampleStats
{
    private static readonly object Gate = new();

    private static long count;
    private static Vector4 min = new(float.PositiveInfinity);
    private static Vector4 max = new(float.NegativeInfinity);

    public readonly struct Snapshot
    {
        public readonly long Count;
        public readonly Vector4 Min;
        public readonly Vector4 Max;

        public Snapshot(long count, Vector4 min, Vector4 max)
        {
            Count = count;
            Min = min;
            Max = max;
        }
    }

    public static void Record(in Vector4 lightSample)
    {
        lock (Gate)
        {
            count++;

            min = new Vector4(
                (float)Math.Min(min.X, lightSample.X),
                (float)Math.Min(min.Y, lightSample.Y),
                (float)Math.Min(min.Z, lightSample.Z),
                (float)Math.Min(min.W, lightSample.W));

            max = new Vector4(
                (float)Math.Max(max.X, lightSample.X),
                (float)Math.Max(max.Y, lightSample.Y),
                (float)Math.Max(max.Z, lightSample.Z),
                (float)Math.Max(max.W, lightSample.W));
        }
    }

    public static bool TryTakeSnapshot(out Snapshot snapshot)
    {
        lock (Gate)
        {
            if (count <= 0)
            {
                snapshot = default;
                return false;
            }

            snapshot = new Snapshot(count, min, max);

            // Reset for the next log interval.
            count = 0;
            min = new Vector4(float.PositiveInfinity);
            max = new Vector4(float.NegativeInfinity);

            return true;
        }
    }
}
