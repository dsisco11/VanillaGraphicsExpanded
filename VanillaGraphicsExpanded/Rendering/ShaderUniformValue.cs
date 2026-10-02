using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Compares exact upload representations and owns mutable ordinary-value snapshots.</summary>
internal static class ShaderUniformValue
{
    #region Public API
    /// <summary>Retains mutable arrays by value; immutable upload values require no copy.</summary>
    internal static T Snapshot<T>(T value) => value is Array array ? (T)(object)array.Clone() : value;

    /// <summary>Compares every uploaded bit, including signed zero and NaN payloads.</summary>
    internal static bool Equal<T>(T left, T right)
    {
        // Typed cases avoid approximate math equality and array reference comparisons.
        return (left, right) switch
        {
            (float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b),
            (Vector2 a, Vector2 b) => Bits(a, b),
            (Vector3 a, Vector3 b) => Bits(a, b),
            (Vector4 a, Vector4 b) => Bits(a, b),
            (Matrix4x4 a, Matrix4x4 b) => Bits(a, b),
            (float[] a, float[] b) => ArrayBits(a, b),
            (int[] a, int[] b) => ArrayBits(a, b),
            (bool[] a, bool[] b) => ArrayBits(a, b),
            (Vector2[] a, Vector2[] b) => ArrayBits(a, b),
            (Vector3[] a, Vector3[] b) => ArrayBits(a, b),
            (Vector4[] a, Vector4[] b) => ArrayBits(a, b),
            (Matrix4x4[] a, Matrix4x4[] b) => ArrayBits(a, b),
            _ => EqualityComparer<T>.Default.Equals(left, right)
        };
    }
    #endregion

    #region Private
    /// <summary>Compares the contiguous representation of one supported numeric value.</summary>
    private static bool Bits<T>(T left, T right) where T : unmanaged =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref left, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref right, 1)));

    /// <summary>Compares immutable snapshots without allocating another copy.</summary>
    private static bool ArrayBits<T>(T[] left, T[] right) where T : unmanaged =>
        MemoryMarshal.AsBytes(left.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(right.AsSpan()));
    #endregion
}
