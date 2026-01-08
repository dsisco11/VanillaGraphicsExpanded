using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Helper methods for converting between VS float[] matrices and System.Numerics.Matrix4x4.
/// VS uses column-major float[16] arrays (OpenGL convention), while System.Numerics uses row-major.
/// </summary>
public static class MatrixHelper
{
    #region Conversion Methods

    /// <summary>
    /// Converts a VS/OpenGL column-major float[16] to Matrix4x4.
    /// </summary>
    /// <param name="m">Column-major float span from VS (e.g., capi.Render.CurrentProjectionMatrix)</param>
    /// <returns>Matrix4x4 representation</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 FromColumnMajor(ReadOnlySpan<float> m)
    {
        return new Matrix4x4(
            m[0], m[4], m[8], m[12],
            m[1], m[5], m[9], m[13],
            m[2], m[6], m[10], m[14],
            m[3], m[7], m[11], m[15]);
    }

    /// <summary>
    /// Converts a Matrix4x4 to VS/OpenGL column-major float[16].
    /// </summary>
    /// <param name="matrix">Matrix4x4 to convert</param>
    /// <param name="result">Pre-allocated float span (minimum 16 elements) to store the result</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToColumnMajor(in Matrix4x4 matrix, Span<float> result)
    {
        result[0] = matrix.M11; result[1] = matrix.M21; result[2] = matrix.M31; result[3] = matrix.M41;
        result[4] = matrix.M12; result[5] = matrix.M22; result[6] = matrix.M32; result[7] = matrix.M42;
        result[8] = matrix.M13; result[9] = matrix.M23; result[10] = matrix.M33; result[11] = matrix.M43;
        result[12] = matrix.M14; result[13] = matrix.M24; result[14] = matrix.M34; result[15] = matrix.M44;
    }

    /// <summary>
    /// Converts a Matrix4x4 to a new VS/OpenGL column-major float[16] array.
    /// </summary>
    /// <param name="matrix">Matrix4x4 to convert</param>
    /// <returns>New float[16] array in column-major order</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float[] ToColumnMajor(in Matrix4x4 matrix)
    {
        var result = new float[16];
        ToColumnMajor(matrix, result);
        return result;
    }

    #endregion

    #region Matrix Operations

    /// <summary>
    /// Computes the inverse of a VS/OpenGL column-major matrix.
    /// </summary>
    /// <param name="m">Column-major float span to invert</param>
    /// <param name="result">Pre-allocated float span (minimum 16 elements) for the inverted matrix</param>
    /// <returns>True if inversion succeeded, false if matrix is singular</returns>
    public static bool Invert(ReadOnlySpan<float> m, Span<float> result)
    {
        var matrix = FromColumnMajor(m);
        if (!Matrix4x4.Invert(matrix, out var inverse))
        {
            // Return identity on failure
            SetIdentity(result);
            return false;
        }
        ToColumnMajor(inverse, result);
        return true;
    }

    /// <summary>
    /// Computes the inverse of a Matrix4x4.
    /// </summary>
    /// <param name="matrix">Matrix to invert</param>
    /// <param name="inverse">Inverted matrix (identity if singular)</param>
    /// <returns>True if inversion succeeded</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Invert(in Matrix4x4 matrix, out Matrix4x4 inverse)
    {
        if (!Matrix4x4.Invert(matrix, out inverse))
        {
            inverse = Matrix4x4.Identity;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Multiplies two VS/OpenGL column-major matrices: result = a * b
    /// </summary>
    /// <param name="a">First matrix (column-major)</param>
    /// <param name="b">Second matrix (column-major)</param>
    /// <param name="result">Pre-allocated float span (minimum 16 elements) for result (column-major)</param>
    public static void Multiply(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> result)
    {
        var matA = FromColumnMajor(a);
        var matB = FromColumnMajor(b);
        var product = matA * matB;
        ToColumnMajor(product, result);
    }

    /// <summary>
    /// Sets a float span to identity matrix.
    /// </summary>
    /// <param name="result">Span to set (minimum 16 elements)</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetIdentity(Span<float> result)
    {
        result[0] = 1f; result[1] = 0f; result[2] = 0f; result[3] = 0f;
        result[4] = 0f; result[5] = 1f; result[6] = 0f; result[7] = 0f;
        result[8] = 0f; result[9] = 0f; result[10] = 1f; result[11] = 0f;
        result[12] = 0f; result[13] = 0f; result[14] = 0f; result[15] = 1f;
    }

    #endregion
}
