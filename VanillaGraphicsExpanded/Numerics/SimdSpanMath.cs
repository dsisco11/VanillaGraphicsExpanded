using System;
using System.Diagnostics;
using System.Numerics.Tensors;

namespace VanillaGraphicsExpanded.Numerics;

internal static class SimdSpanMath
{
    private const float Inv255 = 1f / 255f;

    public static void Fill(Span<float> destination, float value)
    {
        destination.Fill(value);
    }

    public static void Clamp(Span<float> destination, float min, float max)
    {
        TensorPrimitives.Clamp(destination, min, max, destination);
    }

    public static void Clamp01(Span<float> destination)
    {
        Clamp(destination, 0f, 1f);
    }

    public static void ScaleInPlace(Span<float> destination, float scale)
    {
        TensorPrimitives.Multiply(destination, scale, destination);
    }

    public static void Scale(ReadOnlySpan<float> source, float scale, Span<float> destination)
    {
        EnsureSameLength(source.Length, destination.Length);
        TensorPrimitives.Multiply(source, scale, destination);
    }

    public static void AddInPlace(Span<float> destination, float add)
    {
        TensorPrimitives.Add(destination, add, destination);
    }

    public static void Add(ReadOnlySpan<float> source, float add, Span<float> destination)
    {
        EnsureSameLength(source.Length, destination.Length);
        TensorPrimitives.Add(source, add, destination);
    }

    public static void MultiplyAddClamp01(ReadOnlySpan<float> x, float mul, float add, Span<float> destination)
    {
        EnsureSameLength(x.Length, destination.Length);

        TensorPrimitives.Multiply(x, mul, destination);
        TensorPrimitives.Add(destination, add, destination);
        TensorPrimitives.Clamp(destination, 0f, 1f, destination);
    }

    public static void BytesToSingles(ReadOnlySpan<byte> source, Span<float> destination)
    {
        EnsureSameLength(source.Length, destination.Length);
        TensorPrimitives.ConvertTruncating<byte, float>(source, destination);
    }

    public static void BytesToSingles01(ReadOnlySpan<byte> source, Span<float> destination)
    {
        BytesToSingles(source, destination);
        TensorPrimitives.Multiply(destination, Inv255, destination);

        Debug.Assert(destination.Length == source.Length);
    }

    private static void EnsureSameLength(int sourceLength, int destinationLength)
    {
        if (sourceLength != destinationLength)
        {
            throw new ArgumentException($"Length mismatch (source={sourceLength}, destination={destinationLength}).");
        }
    }
}
