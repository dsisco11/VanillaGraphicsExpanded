using System.Numerics;
using System.Runtime.Intrinsics.X86;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>
/// Unit tests for MatrixHelper ensuring all SIMD variants produce correct results.
/// </summary>
public class MatrixHelperTests
{
    private const float Epsilon = 1e-6f;

    #region Test Data

    /// <summary>
    /// Creates a column-major identity matrix as float[16].
    /// </summary>
    private static float[] CreateIdentityColumnMajor() => new float[]
    {
        1, 0, 0, 0,  // column 0
        0, 1, 0, 0,  // column 1
        0, 0, 1, 0,  // column 2
        0, 0, 0, 1   // column 3
    };

    /// <summary>
    /// Creates a known test matrix in column-major format.
    /// Column 0: [1,2,3,4], Column 1: [5,6,7,8], Column 2: [9,10,11,12], Column 3: [13,14,15,16]
    /// </summary>
    private static float[] CreateTestMatrixColumnMajor() => new float[]
    {
        1, 2, 3, 4,      // column 0
        5, 6, 7, 8,      // column 1
        9, 10, 11, 12,   // column 2
        13, 14, 15, 16   // column 3
    };

    /// <summary>
    /// The expected row-major Matrix4x4 for the test matrix.
    /// Row 0: [1,5,9,13], Row 1: [2,6,10,14], Row 2: [3,7,11,15], Row 3: [4,8,12,16]
    /// </summary>
    private static Matrix4x4 CreateExpectedTestMatrix4x4() => new Matrix4x4(
        1, 5, 9, 13,   // row 0
        2, 6, 10, 14,  // row 1
        3, 7, 11, 15,  // row 2
        4, 8, 12, 16   // row 3
    );

    /// <summary>
    /// Creates an invertible matrix in column-major format.
    /// </summary>
    private static float[] CreateInvertibleMatrixColumnMajor() => new float[]
    {
        4, 0, 0, 0,   // column 0
        0, 3, 0, 0,   // column 1
        0, 0, 2, 0,   // column 2
        1, 2, 3, 1    // column 3
    };

    /// <summary>
    /// Creates a singular (non-invertible) matrix in column-major format.
    /// </summary>
    private static float[] CreateSingularMatrixColumnMajor() => new float[]
    {
        1, 2, 3, 4,   // column 0
        2, 4, 6, 8,   // column 1 (2x column 0)
        1, 1, 1, 1,   // column 2
        0, 0, 0, 0    // column 3
    };

    #endregion

    #region FromColumnMajor Tests

    [Fact]
    public void FromColumnMajor_Identity_ReturnsIdentityMatrix()
    {
        var colMajor = CreateIdentityColumnMajor();

        var result = MatrixHelper.FromColumnMajor(colMajor);

        Assert.Equal(Matrix4x4.Identity, result);
    }

    [Fact]
    public void FromColumnMajor_TestMatrix_ReturnsCorrectMatrix()
    {
        var colMajor = CreateTestMatrixColumnMajor();
        var expected = CreateExpectedTestMatrix4x4();

        var result = MatrixHelper.FromColumnMajor(colMajor);

        AssertMatrixEqual(expected, result);
    }

    [Fact]
    public void FromColumnMajorScalar_ReturnsCorrectMatrix()
    {
        var colMajor = CreateTestMatrixColumnMajor();
        var expected = CreateExpectedTestMatrix4x4();

        var result = MatrixHelper.FromColumnMajorScalar(colMajor);

        AssertMatrixEqual(expected, result);
    }

    [SkippableFact]
    public void FromColumnMajorSse_ReturnsCorrectMatrix()
    {
        Skip.IfNot(Sse.IsSupported, "SSE not supported on this platform");

        var colMajor = CreateTestMatrixColumnMajor();
        var expected = CreateExpectedTestMatrix4x4();

        var result = MatrixHelper.FromColumnMajorSse(colMajor);

        AssertMatrixEqual(expected, result);
    }

    [SkippableFact]
    public void FromColumnMajorAvx2_ReturnsCorrectMatrix()
    {
        Skip.IfNot(Avx2.IsSupported, "AVX2 not supported on this platform");

        var colMajor = CreateTestMatrixColumnMajor();
        var expected = CreateExpectedTestMatrix4x4();

        var result = MatrixHelper.FromColumnMajorAvx2(colMajor);

        AssertMatrixEqual(expected, result);
    }

    [SkippableFact]
    public void FromColumnMajor_AllVariantsProduceSameResult()
    {
        var colMajor = CreateTestMatrixColumnMajor();

        var scalar = MatrixHelper.FromColumnMajorScalar(colMajor);

        if (Sse.IsSupported)
        {
            var sse = MatrixHelper.FromColumnMajorSse(colMajor);
            AssertMatrixEqual(scalar, sse, "SSE variant differs from Scalar");
        }

        if (Avx2.IsSupported)
        {
            var avx2 = MatrixHelper.FromColumnMajorAvx2(colMajor);
            AssertMatrixEqual(scalar, avx2, "AVX2 variant differs from Scalar");
        }
    }

    #endregion

    #region ToColumnMajor Tests

    [Fact]
    public void ToColumnMajor_Identity_ReturnsIdentityColumnMajor()
    {
        var expected = CreateIdentityColumnMajor();
        var result = new float[16];

        MatrixHelper.ToColumnMajor(Matrix4x4.Identity, result);

        AssertArrayEqual(expected, result);
    }

    [Fact]
    public void ToColumnMajor_TestMatrix_ReturnsCorrectColumnMajor()
    {
        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();
        var result = new float[16];

        MatrixHelper.ToColumnMajor(matrix, result);

        AssertArrayEqual(expected, result);
    }

    [Fact]
    public void ToColumnMajorScalar_ReturnsCorrectColumnMajor()
    {
        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();
        var result = new float[16];

        MatrixHelper.ToColumnMajorScalar(matrix, result);

        AssertArrayEqual(expected, result);
    }

    [SkippableFact]
    public void ToColumnMajorSse_ReturnsCorrectColumnMajor()
    {
        Skip.IfNot(Sse.IsSupported, "SSE not supported on this platform");

        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();
        var result = new float[16];

        MatrixHelper.ToColumnMajorSse(matrix, result);

        AssertArrayEqual(expected, result);
    }

    [SkippableFact]
    public void ToColumnMajorAvx2_ReturnsCorrectColumnMajor()
    {
        Skip.IfNot(Avx2.IsSupported, "AVX2 not supported on this platform");

        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();
        var result = new float[16];

        MatrixHelper.ToColumnMajorAvx2(matrix, result);

        AssertArrayEqual(expected, result);
    }

    [SkippableFact]
    public void ToColumnMajor_AllVariantsProduceSameResult()
    {
        var matrix = CreateExpectedTestMatrix4x4();
        var scalar = new float[16];
        MatrixHelper.ToColumnMajorScalar(matrix, scalar);

        if (Sse.IsSupported)
        {
            var sse = new float[16];
            MatrixHelper.ToColumnMajorSse(matrix, sse);
            AssertArrayEqual(scalar, sse, "SSE variant differs from Scalar");
        }

        if (Avx2.IsSupported)
        {
            var avx2 = new float[16];
            MatrixHelper.ToColumnMajorAvx2(matrix, avx2);
            AssertArrayEqual(scalar, avx2, "AVX2 variant differs from Scalar");
        }
    }

    [Fact]
    public void ToColumnMajor_ReturnsNewArray_ReturnsCorrectColumnMajor()
    {
        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();

        var result = MatrixHelper.ToColumnMajor(matrix);

        Assert.Equal(16, result.Length);
        AssertArrayEqual(expected, result);
    }

    #endregion

    #region Roundtrip Tests

    [Fact]
    public void FromColumnMajor_ToColumnMajor_Roundtrip_PreservesData()
    {
        var original = CreateTestMatrixColumnMajor();

        var matrix = MatrixHelper.FromColumnMajor(original);
        var result = new float[16];
        MatrixHelper.ToColumnMajor(matrix, result);

        AssertArrayEqual(original, result);
    }

    [Fact]
    public void ToColumnMajor_FromColumnMajor_Roundtrip_PreservesData()
    {
        var original = CreateExpectedTestMatrix4x4();

        var colMajor = new float[16];
        MatrixHelper.ToColumnMajor(original, colMajor);
        var result = MatrixHelper.FromColumnMajor(colMajor);

        AssertMatrixEqual(original, result);
    }

    #endregion

    #region SetIdentity Tests

    [Fact]
    public void SetIdentity_SetsIdentityMatrix()
    {
        var expected = CreateIdentityColumnMajor();
        var result = new float[16];

        // Fill with garbage first
        for (int i = 0; i < 16; i++) result[i] = 99f;

        MatrixHelper.SetIdentity(result);

        AssertArrayEqual(expected, result);
    }

    [Fact]
    public void SetIdentityScalar_SetsIdentityMatrix()
    {
        var expected = CreateIdentityColumnMajor();
        var result = new float[16];
        for (int i = 0; i < 16; i++) result[i] = 99f;

        MatrixHelper.SetIdentityScalar(result);

        AssertArrayEqual(expected, result);
    }

    [SkippableFact]
    public void SetIdentitySse_SetsIdentityMatrix()
    {
        Skip.IfNot(Sse.IsSupported, "SSE not supported on this platform");

        var expected = CreateIdentityColumnMajor();
        var result = new float[16];
        for (int i = 0; i < 16; i++) result[i] = 99f;

        MatrixHelper.SetIdentitySse(result);

        AssertArrayEqual(expected, result);
    }

    [SkippableFact]
    public void SetIdentityAvx_SetsIdentityMatrix()
    {
        Skip.IfNot(Avx.IsSupported, "AVX not supported on this platform");

        var expected = CreateIdentityColumnMajor();
        var result = new float[16];
        for (int i = 0; i < 16; i++) result[i] = 99f;

        MatrixHelper.SetIdentityAvx(result);

        AssertArrayEqual(expected, result);
    }

    [SkippableFact]
    public void SetIdentity_AllVariantsProduceSameResult()
    {
        var scalar = new float[16];
        MatrixHelper.SetIdentityScalar(scalar);

        if (Sse.IsSupported)
        {
            var sse = new float[16];
            MatrixHelper.SetIdentitySse(sse);
            AssertArrayEqual(scalar, sse, "SSE variant differs from Scalar");
        }

        if (Avx.IsSupported)
        {
            var avx = new float[16];
            MatrixHelper.SetIdentityAvx(avx);
            AssertArrayEqual(scalar, avx, "AVX variant differs from Scalar");
        }
    }

    #endregion

    #region Invert Tests

    [Fact]
    public void Invert_Identity_ReturnsIdentity()
    {
        var identity = CreateIdentityColumnMajor();
        var result = new float[16];

        var success = MatrixHelper.Invert(identity, result);

        Assert.True(success);
        AssertArrayEqual(identity, result);
    }

    [Fact]
    public void Invert_InvertibleMatrix_ReturnsInverse()
    {
        var original = CreateInvertibleMatrixColumnMajor();
        var inverse = new float[16];

        var success = MatrixHelper.Invert(original, inverse);

        Assert.True(success);

        // Verify: original * inverse = identity
        var product = new float[16];
        MatrixHelper.Multiply(original, inverse, product);

        var identity = CreateIdentityColumnMajor();
        AssertArrayEqual(identity, product, tolerance: 1e-5f);
    }

    [Fact]
    public void Invert_SingularMatrix_ReturnsFalseAndIdentity()
    {
        var singular = CreateSingularMatrixColumnMajor();
        var result = new float[16];
        for (int i = 0; i < 16; i++) result[i] = 99f;

        var success = MatrixHelper.Invert(singular, result);

        Assert.False(success);
        AssertArrayEqual(CreateIdentityColumnMajor(), result);
    }

    [Fact]
    public void Invert_Matrix4x4_InvertibleMatrix_ReturnsInverse()
    {
        var original = new Matrix4x4(
            4, 0, 0, 1,
            0, 3, 0, 2,
            0, 0, 2, 3,
            0, 0, 0, 1
        );

        var success = MatrixHelper.Invert(original, out var inverse);

        Assert.True(success);

        // Verify: original * inverse = identity
        var product = original * inverse;
        AssertMatrixEqual(Matrix4x4.Identity, product, tolerance: 1e-5f);
    }

    [Fact]
    public void Invert_Matrix4x4_SingularMatrix_ReturnsFalseAndIdentity()
    {
        var singular = new Matrix4x4(
            1, 2, 1, 0,
            2, 4, 1, 0,
            3, 6, 1, 0,
            4, 8, 1, 0
        );

        var success = MatrixHelper.Invert(singular, out var inverse);

        Assert.False(success);
        Assert.Equal(Matrix4x4.Identity, inverse);
    }

    #endregion

    #region Multiply Tests

    [Fact]
    public void Multiply_ByIdentity_ReturnsSameMatrix()
    {
        var matrix = CreateTestMatrixColumnMajor();
        var identity = CreateIdentityColumnMajor();
        var result = new float[16];

        MatrixHelper.Multiply(matrix, identity, result);

        AssertArrayEqual(matrix, result);
    }

    [Fact]
    public void Multiply_IdentityBy_ReturnsSameMatrix()
    {
        var matrix = CreateTestMatrixColumnMajor();
        var identity = CreateIdentityColumnMajor();
        var result = new float[16];

        MatrixHelper.Multiply(identity, matrix, result);

        AssertArrayEqual(matrix, result);
    }

    [Fact]
    public void Multiply_KnownMatrices_ReturnsCorrectProduct()
    {
        // A = scaling by 2
        var a = new float[] { 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 1 };
        // B = translation by (1, 2, 3)
        var b = new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 2, 3, 1 };

        var result = new float[16];
        MatrixHelper.Multiply(a, b, result);

        // Verify using Matrix4x4
        var matA = MatrixHelper.FromColumnMajor(a);
        var matB = MatrixHelper.FromColumnMajor(b);
        var expected = matA * matB;

        var resultMat = MatrixHelper.FromColumnMajor(result);
        AssertMatrixEqual(expected, resultMat);
    }

    #endregion

    #region Assertion Helpers

    private static void AssertMatrixEqual(Matrix4x4 expected, Matrix4x4 actual, string? message = null, float tolerance = Epsilon)
    {
        var msg = message ?? "Matrices are not equal";
        Assert.True(Math.Abs(expected.M11 - actual.M11) < tolerance, $"{msg}: M11 differs");
        Assert.True(Math.Abs(expected.M12 - actual.M12) < tolerance, $"{msg}: M12 differs");
        Assert.True(Math.Abs(expected.M13 - actual.M13) < tolerance, $"{msg}: M13 differs");
        Assert.True(Math.Abs(expected.M14 - actual.M14) < tolerance, $"{msg}: M14 differs");
        Assert.True(Math.Abs(expected.M21 - actual.M21) < tolerance, $"{msg}: M21 differs");
        Assert.True(Math.Abs(expected.M22 - actual.M22) < tolerance, $"{msg}: M22 differs");
        Assert.True(Math.Abs(expected.M23 - actual.M23) < tolerance, $"{msg}: M23 differs");
        Assert.True(Math.Abs(expected.M24 - actual.M24) < tolerance, $"{msg}: M24 differs");
        Assert.True(Math.Abs(expected.M31 - actual.M31) < tolerance, $"{msg}: M31 differs");
        Assert.True(Math.Abs(expected.M32 - actual.M32) < tolerance, $"{msg}: M32 differs");
        Assert.True(Math.Abs(expected.M33 - actual.M33) < tolerance, $"{msg}: M33 differs");
        Assert.True(Math.Abs(expected.M34 - actual.M34) < tolerance, $"{msg}: M34 differs");
        Assert.True(Math.Abs(expected.M41 - actual.M41) < tolerance, $"{msg}: M41 differs");
        Assert.True(Math.Abs(expected.M42 - actual.M42) < tolerance, $"{msg}: M42 differs");
        Assert.True(Math.Abs(expected.M43 - actual.M43) < tolerance, $"{msg}: M43 differs");
        Assert.True(Math.Abs(expected.M44 - actual.M44) < tolerance, $"{msg}: M44 differs");
    }

    private static void AssertArrayEqual(float[] expected, float[] actual, string? message = null, float tolerance = Epsilon)
    {
        var msg = message ?? "Arrays are not equal";
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) < tolerance,
                $"{msg}: Element [{i}] differs - expected {expected[i]}, got {actual[i]}");
        }
    }

    #endregion
}
