using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Helper methods for converting between VS float[] matrices and System.Numerics.Matrix4x4.
/// VS uses column-major float[16] arrays (OpenGL convention), while System.Numerics uses row-major.
/// Uses SIMD intrinsics for optimized transpose operations where available.
/// </summary>
public static class MatrixHelper
{
    #region Conversion Methods

    /// <summary>
    /// Converts a VS/OpenGL column-major float[16] to Matrix4x4.
    /// Uses SIMD transpose when SSE is available.
    /// </summary>
    /// <param name="m">Column-major float span from VS (e.g., capi.Render.CurrentProjectionMatrix)</param>
    /// <returns>Matrix4x4 representation</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 FromColumnMajor(ReadOnlySpan<float> m)
    {
        if (Avx2.IsSupported && m.Length >= 16)
        {
            return FromColumnMajorAvx2(m);
        }
        if (Sse.IsSupported && m.Length >= 16)
        {
            return FromColumnMajorSse(m);
        }
        return FromColumnMajorScalar(m);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Matrix4x4 FromColumnMajorScalar(ReadOnlySpan<float> m)
    {
        return new Matrix4x4(
            m[0], m[4], m[8], m[12],
            m[1], m[5], m[9], m[13],
            m[2], m[6], m[10], m[14],
            m[3], m[7], m[11], m[15]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Matrix4x4 FromColumnMajorAvx2(ReadOnlySpan<float> m)
    {
        fixed (float* ptr = m)
        {
            // Load columns in pairs: [c0,c1] and [c2,c3]
            var v0 = Avx.LoadVector256(ptr);     // [c0.xyzw | c1.xyzw]
            var v1 = Avx.LoadVector256(ptr + 8); // [c2.xyzw | c3.xyzw]

            // Stage 1: Interleave within 128-bit lanes
            var t0 = Avx.UnpackLow(v0, v1);   // [c0.x,c2.x,c0.y,c2.y | c1.x,c3.x,c1.y,c3.y]
            var t1 = Avx.UnpackHigh(v0, v1);  // [c0.z,c2.z,c0.w,c2.w | c1.z,c3.z,c1.w,c3.w]

            // Stage 2: Swap lanes to group even/odd columns
            var p0 = Avx2.Permute2x128(t0, t1, 0x20); // [c0.x,c2.x,c0.y,c2.y | c0.z,c2.z,c0.w,c2.w]
            var p1 = Avx2.Permute2x128(t0, t1, 0x31); // [c1.x,c3.x,c1.y,c3.y | c1.z,c3.z,c1.w,c3.w]

            // Stage 3: Final interleave to get rows
            var r02 = Avx.UnpackLow(p0, p1);  // [row0 | row2]
            var r13 = Avx.UnpackHigh(p0, p1); // [row1 | row3]

            // Stage 4: Permute to row order
            var r01 = Avx2.Permute2x128(r02, r13, 0x20); // [row0 | row1]
            var r23 = Avx2.Permute2x128(r02, r13, 0x31); // [row2 | row3]

            Matrix4x4 result;
            Avx.Store((float*)&result, r01);
            Avx.Store((float*)&result + 8, r23);
            return result;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Matrix4x4 FromColumnMajorSse(ReadOnlySpan<float> m)
    {
        fixed (float* ptr = m)
        {
            // Load 4 columns
            var col0 = Sse.LoadVector128(ptr);
            var col1 = Sse.LoadVector128(ptr + 4);
            var col2 = Sse.LoadVector128(ptr + 8);
            var col3 = Sse.LoadVector128(ptr + 12);

            // Transpose 4x4 matrix using SSE shuffles
            // First pass: interleave pairs
            var tmp0 = Sse.UnpackLow(col0, col1);   // [c0.x, c1.x, c0.y, c1.y]
            var tmp1 = Sse.UnpackHigh(col0, col1);  // [c0.z, c1.z, c0.w, c1.w]
            var tmp2 = Sse.UnpackLow(col2, col3);   // [c2.x, c3.x, c2.y, c3.y]
            var tmp3 = Sse.UnpackHigh(col2, col3);  // [c2.z, c3.z, c2.w, c3.w]

            // Second pass: move floats to final positions
            var row0 = Sse.MoveLowToHigh(tmp0, tmp2);  // [c0.x, c1.x, c2.x, c3.x] = row0
            var row1 = Sse.MoveHighToLow(tmp2, tmp0);  // [c0.y, c1.y, c2.y, c3.y] = row1
            var row2 = Sse.MoveLowToHigh(tmp1, tmp3);  // [c0.z, c1.z, c2.z, c3.z] = row2
            var row3 = Sse.MoveHighToLow(tmp3, tmp1);  // [c0.w, c1.w, c2.w, c3.w] = row3

            // Store directly into Matrix4x4
            Matrix4x4 result;
            Sse.Store((float*)&result, row0);
            Sse.Store((float*)&result + 4, row1);
            Sse.Store((float*)&result + 8, row2);
            Sse.Store((float*)&result + 12, row3);
            return result;
        }
    }

    /// <summary>
    /// Converts a Matrix4x4 to VS/OpenGL column-major float[16].
    /// Uses SIMD transpose when SSE is available.
    /// </summary>
    /// <param name="matrix">Matrix4x4 to convert</param>
    /// <param name="result">Pre-allocated float span (minimum 16 elements) to store the result</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToColumnMajor(in Matrix4x4 matrix, Span<float> result)
    {
        if (Avx2.IsSupported && result.Length >= 16)
        {
            ToColumnMajorAvx2(in matrix, result);
        }
        else if (Sse.IsSupported && result.Length >= 16)
        {
            ToColumnMajorSse(in matrix, result);
        }
        else
        {
            ToColumnMajorScalar(in matrix, result);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToColumnMajorScalar(in Matrix4x4 matrix, Span<float> result)
    {
        result[0] = matrix.M11; result[1] = matrix.M21; result[2] = matrix.M31; result[3] = matrix.M41;
        result[4] = matrix.M12; result[5] = matrix.M22; result[6] = matrix.M32; result[7] = matrix.M42;
        result[8] = matrix.M13; result[9] = matrix.M23; result[10] = matrix.M33; result[11] = matrix.M43;
        result[12] = matrix.M14; result[13] = matrix.M24; result[14] = matrix.M34; result[15] = matrix.M44;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToColumnMajorAvx2(in Matrix4x4 matrix, Span<float> result)
    {
        fixed (Matrix4x4* matPtr = &matrix)
        fixed (float* resPtr = result)
        {
            // Load rows in pairs: [row0,row1] and [row2,row3]
            var v0 = Avx.LoadVector256((float*)matPtr);     // [r0.xyzw | r1.xyzw]
            var v1 = Avx.LoadVector256((float*)matPtr + 8); // [r2.xyzw | r3.xyzw]

            // Stage 1: Interleave within 128-bit lanes
            var t0 = Avx.UnpackLow(v0, v1);   // [r0.x,r2.x,r0.y,r2.y | r1.x,r3.x,r1.y,r3.y]
            var t1 = Avx.UnpackHigh(v0, v1);  // [r0.z,r2.z,r0.w,r2.w | r1.z,r3.z,r1.w,r3.w]

            // Stage 2: Swap lanes to group even/odd rows
            var p0 = Avx2.Permute2x128(t0, t1, 0x20); // [r0.x,r2.x,r0.y,r2.y | r0.z,r2.z,r0.w,r2.w]
            var p1 = Avx2.Permute2x128(t0, t1, 0x31); // [r1.x,r3.x,r1.y,r3.y | r1.z,r3.z,r1.w,r3.w]

            // Stage 3: Final interleave to get columns
            var c02 = Avx.UnpackLow(p0, p1);  // [col0 | col2]
            var c13 = Avx.UnpackHigh(p0, p1); // [col1 | col3]

            // Stage 4: Permute to column order
            var c01 = Avx2.Permute2x128(c02, c13, 0x20); // [col0 | col1]
            var c23 = Avx2.Permute2x128(c02, c13, 0x31); // [col2 | col3]

            Avx.Store(resPtr, c01);
            Avx.Store(resPtr + 8, c23);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToColumnMajorSse(in Matrix4x4 matrix, Span<float> result)
    {
        fixed (Matrix4x4* matPtr = &matrix)
        fixed (float* resPtr = result)
        {
            // Load 4 rows from Matrix4x4
            var row0 = Sse.LoadVector128((float*)matPtr);
            var row1 = Sse.LoadVector128((float*)matPtr + 4);
            var row2 = Sse.LoadVector128((float*)matPtr + 8);
            var row3 = Sse.LoadVector128((float*)matPtr + 12);

            // Transpose 4x4 matrix using SSE shuffles
            var tmp0 = Sse.UnpackLow(row0, row1);   // [r0.x, r1.x, r0.y, r1.y]
            var tmp1 = Sse.UnpackHigh(row0, row1);  // [r0.z, r1.z, r0.w, r1.w]
            var tmp2 = Sse.UnpackLow(row2, row3);   // [r2.x, r3.x, r2.y, r3.y]
            var tmp3 = Sse.UnpackHigh(row2, row3);  // [r2.z, r3.z, r2.w, r3.w]

            var col0 = Sse.MoveLowToHigh(tmp0, tmp2);  // [r0.x, r1.x, r2.x, r3.x] = col0
            var col1 = Sse.MoveHighToLow(tmp2, tmp0);  // [r0.y, r1.y, r2.y, r3.y] = col1
            var col2 = Sse.MoveLowToHigh(tmp1, tmp3);  // [r0.z, r1.z, r2.z, r3.z] = col2
            var col3 = Sse.MoveHighToLow(tmp3, tmp1);  // [r0.w, r1.w, r2.w, r3.w] = col3

            // Store transposed columns
            Sse.Store(resPtr, col0);
            Sse.Store(resPtr + 4, col1);
            Sse.Store(resPtr + 8, col2);
            Sse.Store(resPtr + 12, col3);
        }
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
        ToColumnMajor(in matrix, result);
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
        ToColumnMajor(in inverse, result);
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
        ToColumnMajor(in product, result);
    }

    /// <summary>
    /// Sets a float span to identity matrix.
    /// Uses SIMD stores when SSE is available.
    /// </summary>
    /// <param name="result">Span to set (minimum 16 elements)</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetIdentity(Span<float> result)
    {
        if (Avx.IsSupported && result.Length >= 16)
        {
            SetIdentityAvx(result);
        }
        else if (Sse.IsSupported && result.Length >= 16)
        {
            SetIdentitySse(result);
        }
        else
        {
            SetIdentityScalar(result);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetIdentityScalar(Span<float> result)
    {// using column-major order
        result.Clear(); // clears 64 bytes very efficiently

        // Use refs to encourage bounds-check elimination
        ref float r = ref MemoryMarshal.GetReference(result);
        r = 1f;
        Unsafe.Add(ref r, 5)  = 1f;
        Unsafe.Add(ref r, 10) = 1f;
        Unsafe.Add(ref r, 15) = 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void SetIdentitySse(Span<float> result)
    {
        fixed (float* ptr = result)
        {
            // one = [1, 0, 0, 0] as int bits
            var oneI = Vector128.Create(unchecked((int)0x3f800000));
            var c0 = oneI;
            var c1 = Sse2.ShiftLeftLogical128BitLane(oneI, 4);
            var c2 = Sse2.ShiftLeftLogical128BitLane(oneI, 8);
            var c3 = Sse2.ShiftLeftLogical128BitLane(oneI, 12);

            Sse.Store(ptr +  0, c0.AsSingle());
            Sse.Store(ptr +  4, c1.AsSingle());
            Sse.Store(ptr +  8, c2.AsSingle());
            Sse.Store(ptr + 12, c3.AsSingle());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void SetIdentityAvx(Span<float> result)
    {
        fixed (float* ptr = result)
        {
            Avx.Store(ptr,     Vector256.Create(1f, 0f, 0f, 0f,   0f, 1f, 0f, 0f));
            Avx.Store(ptr + 8, Vector256.Create(0f, 0f, 1f, 0f,   0f, 0f, 0f, 1f));
        }
    }


    #endregion
}
