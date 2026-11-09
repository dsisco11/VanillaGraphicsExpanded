using System.Numerics;
using System.Runtime.Intrinsics.X86;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Numerics;

/// <summary>
/// Unit tests for MatrixHelper ensuring all SIMD variants produce correct results.
/// </summary>
public class MatrixHelperTests
{
    #region Public API


    #region FromColumnMajor Tests

    /// <summary>Converts column-major data into the expected matrix representation.</summary>
    [Fact]
    public void FromColumnMajor_Identity_ReturnsIdentityMatrix()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var colMajor = CreateIdentityColumnMajor();

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var result = MatrixHelper.FromColumnMajor(colMajor);

        // Assert: the result matches the independent matrix contract.
        Assert.Equal(Matrix4x4.Identity, result);
    }

    /// <summary>Converts column-major data into the expected matrix representation.</summary>
    [Fact]
    public void FromColumnMajor_TestMatrix_ReturnsCorrectMatrix()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var colMajor = CreateTestMatrixColumnMajor();
        var expected = CreateExpectedTestMatrix4x4();

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var result = MatrixHelper.FromColumnMajor(colMajor);

        // Assert: the result matches the independent matrix contract.
        AssertMatrixEqual(expected, result);
    }

    /// <summary>Converts column-major data into the expected matrix representation.</summary>
    [Fact]
    public void FromColumnMajorScalar_ReturnsCorrectMatrix()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var colMajor = CreateTestMatrixColumnMajor();
        var expected = CreateExpectedTestMatrix4x4();

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var result = MatrixHelper.FromColumnMajorScalar(colMajor);

        // Assert: the result matches the independent matrix contract.
        AssertMatrixEqual(expected, result);
    }

    /// <summary>Converts column-major data into the expected matrix representation.</summary>
    [Fact]
    public void FromColumnMajorSse_ReturnsCorrectMatrix()
    {
        // Arrange: the platform guard is a precondition; unsupported hardware skips this SIMD-specific case.
        Assert.SkipWhen(!Sse.IsSupported, "SSE not supported on this platform");
        var colMajor = CreateTestMatrixColumnMajor();
        var expected = CreateExpectedTestMatrix4x4();

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var result = MatrixHelper.FromColumnMajorSse(colMajor);

        // Assert: compare the SIMD result to the independent expected matrix.
        AssertMatrixEqual(expected, result);
    }

    /// <summary>Converts column-major data into the expected matrix representation.</summary>
    [Fact]
    public void FromColumnMajorAvx2_ReturnsCorrectMatrix()
    {
        // Arrange: the platform guard is a precondition; unsupported hardware skips this SIMD-specific case.
        Assert.SkipWhen(!Avx2.IsSupported, "AVX2 not supported on this platform");
        var colMajor = CreateTestMatrixColumnMajor();
        var expected = CreateExpectedTestMatrix4x4();

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var result = MatrixHelper.FromColumnMajorAvx2(colMajor);

        // Assert: compare the SIMD result to the independent expected matrix.
        AssertMatrixEqual(expected, result);
    }

    /// <summary>Converts column-major data into the expected matrix representation.</summary>
    [Fact]
    public void FromColumnMajor_AllVariantsProduceSameResult()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var colMajor = CreateTestMatrixColumnMajor();

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var scalar = MatrixHelper.FromColumnMajorScalar(colMajor);

        if (Sse.IsSupported)
        {
            var sse = MatrixHelper.FromColumnMajorSse(colMajor);
        // Assert: the identity conversion writes the expected 16 column-major values.
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

    /// <summary>Writes the expected column-major representation for the supplied matrix.</summary>
    [Fact]
    public void ToColumnMajor_Identity_ReturnsIdentityColumnMajor()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var expected = CreateIdentityColumnMajor();
        var result = new float[16];

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.ToColumnMajor(Matrix4x4.Identity, result);

        // Assert: the identity conversion writes the expected 16 column-major values.
        AssertArrayEqual(expected, result);
    }

    /// <summary>Writes the expected column-major representation for the supplied matrix.</summary>
    [Fact]
    public void ToColumnMajor_TestMatrix_ReturnsCorrectColumnMajor()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();
        var result = new float[16];

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.ToColumnMajor(matrix, result);

        // Assert: the result matches the independent matrix contract.
        AssertArrayEqual(expected, result);
    }

    /// <summary>Writes the expected column-major representation for the supplied matrix.</summary>
    [Fact]
    public void ToColumnMajorScalar_ReturnsCorrectColumnMajor()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();
        var result = new float[16];

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.ToColumnMajorScalar(matrix, result);

        // Assert: the result matches the independent matrix contract.
        AssertArrayEqual(expected, result);
    }

    /// <summary>Writes the expected column-major representation for the supplied matrix.</summary>
    [Fact]
    public void ToColumnMajorSse_ReturnsCorrectColumnMajor()
    {
        // Arrange: the platform guard is a precondition; unsupported hardware skips this SIMD-specific case.
        Assert.SkipWhen(!Sse.IsSupported, "SSE not supported on this platform");
        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();
        var result = new float[16];

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.ToColumnMajorSse(matrix, result);

        // Assert: compare every destination component with the expected representation.
        AssertArrayEqual(expected, result);
    }

    /// <summary>Writes the expected column-major representation for the supplied matrix.</summary>
    [Fact]
    public void ToColumnMajorAvx2_ReturnsCorrectColumnMajor()
    {
        // Arrange: the platform guard is a precondition; unsupported hardware skips this SIMD-specific case.
        Assert.SkipWhen(!Avx2.IsSupported, "AVX2 not supported on this platform");
        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();
        var result = new float[16];

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.ToColumnMajorAvx2(matrix, result);

        // Assert: compare every destination component with the expected representation.
        AssertArrayEqual(expected, result);
    }

    /// <summary>Writes the expected column-major representation for the supplied matrix.</summary>
    [Fact]
    public void ToColumnMajor_AllVariantsProduceSameResult()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var matrix = CreateExpectedTestMatrix4x4();
        var scalar = new float[16];
        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.ToColumnMajorScalar(matrix, scalar);

        if (Sse.IsSupported)
        {
            var sse = new float[16];
            MatrixHelper.ToColumnMajorSse(matrix, sse);
            // Assert: compare this supported SIMD implementation with the scalar result.
            AssertArrayEqual(scalar, sse, "SSE variant differs from Scalar");
        }

        if (Avx2.IsSupported)
        {
            var avx2 = new float[16];
            MatrixHelper.ToColumnMajorAvx2(matrix, avx2);
            AssertArrayEqual(scalar, avx2, "AVX2 variant differs from Scalar");
        }
    }

    /// <summary>Writes the expected column-major representation for the supplied matrix.</summary>
    [Fact]
    public void ToColumnMajor_ReturnsNewArray_ReturnsCorrectColumnMajor()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var matrix = CreateExpectedTestMatrix4x4();
        var expected = CreateTestMatrixColumnMajor();

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var result = MatrixHelper.ToColumnMajor(matrix);

        // Assert: the result matches the independent matrix contract.
        Assert.Equal(16, result.Length);
        AssertArrayEqual(expected, result);
    }

    #endregion

    #region Roundtrip Tests

    /// <summary>Converts column-major data into the expected matrix representation.</summary>
    [Fact]
    public void FromColumnMajor_ToColumnMajor_Roundtrip_PreservesData()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var original = CreateTestMatrixColumnMajor();

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var matrix = MatrixHelper.FromColumnMajor(original);
        var result = new float[16];
        MatrixHelper.ToColumnMajor(matrix, result);

        // Assert: the result matches the independent matrix contract.
        AssertArrayEqual(original, result);
    }

    /// <summary>Writes the expected column-major representation for the supplied matrix.</summary>
    [Fact]
    public void ToColumnMajor_FromColumnMajor_Roundtrip_PreservesData()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var original = CreateExpectedTestMatrix4x4();

        var colMajor = new float[16];
        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.ToColumnMajor(original, colMajor);
        var result = MatrixHelper.FromColumnMajor(colMajor);

        // Assert: the result matches the independent matrix contract.
        AssertMatrixEqual(original, result);
    }

    #endregion

    #region SetIdentity Tests

    /// <summary>Fills the destination with the column-major identity matrix.</summary>
    [Fact]
    public void SetIdentity_SetsIdentityMatrix()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var expected = CreateIdentityColumnMajor();
        var result = new float[16];

        // Fill with garbage first
        for (int i = 0; i < 16; i++) result[i] = 99f;

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.SetIdentity(result);

        // Assert: the result matches the independent matrix contract.
        AssertArrayEqual(expected, result);
    }

    /// <summary>Fills the destination with the column-major identity matrix.</summary>
    [Fact]
    public void SetIdentityScalar_SetsIdentityMatrix()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var expected = CreateIdentityColumnMajor();
        var result = new float[16];
        for (int i = 0; i < 16; i++) result[i] = 99f;

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.SetIdentityScalar(result);

        // Assert: the result matches the independent matrix contract.
        AssertArrayEqual(expected, result);
    }

    /// <summary>Fills the destination with the column-major identity matrix.</summary>
    [Fact]
    public void SetIdentitySse_SetsIdentityMatrix()
    {
        // Arrange: the platform guard is a precondition; unsupported hardware skips this SIMD-specific case.
        Assert.SkipWhen(!Sse.IsSupported, "SSE not supported on this platform");
        var expected = CreateIdentityColumnMajor();
        var result = new float[16];
        for (int i = 0; i < 16; i++) result[i] = 99f;

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.SetIdentitySse(result);

        // Assert: the SIMD path writes the complete column-major identity.
        AssertArrayEqual(expected, result);
    }

    /// <summary>Fills the destination with the column-major identity matrix.</summary>
    [Fact]
    public void SetIdentityAvx_SetsIdentityMatrix()
    {
        // Arrange: the platform guard is a precondition; unsupported hardware skips this SIMD-specific case.
        Assert.SkipWhen(!Avx.IsSupported, "AVX not supported on this platform");
        var expected = CreateIdentityColumnMajor();
        var result = new float[16];
        for (int i = 0; i < 16; i++) result[i] = 99f;

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.SetIdentityAvx(result);

        // Assert: the SIMD path writes the complete column-major identity.
        AssertArrayEqual(expected, result);
    }

    /// <summary>Fills the destination with the column-major identity matrix.</summary>
    [Fact]
    public void SetIdentity_AllVariantsProduceSameResult()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var scalar = new float[16];
        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.SetIdentityScalar(scalar);

        if (Sse.IsSupported)
        {
            var sse = new float[16];
            MatrixHelper.SetIdentitySse(sse);
            // Assert: compare this supported SIMD implementation with the scalar result.
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

    /// <summary>Reports invertibility and returns the expected inverse or identity fallback.</summary>
    [Fact]
    public void Invert_Identity_ReturnsIdentity()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var identity = CreateIdentityColumnMajor();
        var result = new float[16];

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var success = MatrixHelper.Invert(identity, result);

        // Assert: the result matches the independent matrix contract.
        Assert.True(success);
        AssertArrayEqual(identity, result);
    }

    /// <summary>Reports invertibility and returns the expected inverse or identity fallback.</summary>
    [Fact]
    public void Invert_InvertibleMatrix_ReturnsInverse()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var original = CreateInvertibleMatrixColumnMajor();
        var inverse = new float[16];

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var success = MatrixHelper.Invert(original, inverse);

        // Assert: the result matches the independent matrix contract.
        Assert.True(success);

        // Verify: original * inverse = identity
        var product = new float[16];
        MatrixHelper.Multiply(original, inverse, product);

        var identity = CreateIdentityColumnMajor();
        AssertArrayEqual(identity, product, tolerance: 1e-5f);
    }

    /// <summary>Reports invertibility and returns the expected inverse or identity fallback.</summary>
    [Fact]
    public void Invert_SingularMatrix_ReturnsFalseAndIdentity()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var singular = CreateSingularMatrixColumnMajor();
        var result = new float[16];
        for (int i = 0; i < 16; i++) result[i] = 99f;

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var success = MatrixHelper.Invert(singular, result);

        // Assert: the result matches the independent matrix contract.
        Assert.False(success);
        AssertArrayEqual(CreateIdentityColumnMajor(), result);
    }

    /// <summary>Reports invertibility and returns the expected inverse or identity fallback.</summary>
    [Fact]
    public void Invert_Matrix4x4_InvertibleMatrix_ReturnsInverse()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var original = new Matrix4x4(
            4, 0, 0, 1,
            0, 3, 0, 2,
            0, 0, 2, 3,
            0, 0, 0, 1
        );

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var success = MatrixHelper.Invert(original, out var inverse);

        // Assert: the result matches the independent matrix contract.
        Assert.True(success);

        // Verify: original * inverse = identity
        var product = original * inverse;
        AssertMatrixEqual(Matrix4x4.Identity, product, tolerance: 1e-5f);
    }

    /// <summary>Reports invertibility and returns the expected inverse or identity fallback.</summary>
    [Fact]
    public void Invert_Matrix4x4_SingularMatrix_ReturnsFalseAndIdentity()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var singular = new Matrix4x4(
            1, 2, 1, 0,
            2, 4, 1, 0,
            3, 6, 1, 0,
            4, 8, 1, 0
        );

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        var success = MatrixHelper.Invert(singular, out var inverse);

        // Assert: the result matches the independent matrix contract.
        Assert.False(success);
        Assert.Equal(Matrix4x4.Identity, inverse);
    }

    #endregion

    #region Multiply Tests

    /// <summary>Multiplies column-major matrices and preserves the expected product.</summary>
    [Fact]
    public void Multiply_ByIdentity_ReturnsSameMatrix()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var matrix = CreateTestMatrixColumnMajor();
        var identity = CreateIdentityColumnMajor();
        var result = new float[16];

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.Multiply(matrix, identity, result);

        // Assert: the result matches the independent matrix contract.
        AssertArrayEqual(matrix, result);
    }

    /// <summary>Multiplies column-major matrices and preserves the expected product.</summary>
    [Fact]
    public void Multiply_IdentityBy_ReturnsSameMatrix()
    {
        // Arrange: establish matrix inputs and the expected representation.
        var matrix = CreateTestMatrixColumnMajor();
        var identity = CreateIdentityColumnMajor();
        var result = new float[16];

        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.Multiply(identity, matrix, result);

        // Assert: the result matches the independent matrix contract.
        AssertArrayEqual(matrix, result);
    }

    /// <summary>Multiplies column-major matrices and preserves the expected product.</summary>
    [Fact]
    public void Multiply_KnownMatrices_ReturnsCorrectProduct()
    {
        // Arrange: establish matrix inputs and the expected representation.
        // A = scaling by 2
        var a = new float[] { 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 1 };
        // B = translation by (1, 2, 3)
        var b = new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 2, 3, 1 };

        var result = new float[16];
        // Act: apply the conversion, identity, inversion, or multiplication behavior.
        MatrixHelper.Multiply(a, b, result);

        // Verify using Matrix4x4
        var matA = MatrixHelper.FromColumnMajor(a);
        var matB = MatrixHelper.FromColumnMajor(b);
        var expected = matA * matB;

        var resultMat = MatrixHelper.FromColumnMajor(result);
        // Assert: the result matches the independent matrix contract.
        AssertMatrixEqual(expected, resultMat);
    }

    #endregion

    #endregion

    #region Private

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

    #region Assertions

    /// <summary>Checks each row and column against a tolerance and reports the first differing component.</summary>
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

    /// <summary>Checks every array element against the expected value within the supplied tolerance.</summary>
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

    #endregion
}
