using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies shared frame capture values and atomic CPU snapshot publication.</summary>
public sealed class VgeFrameUniformBufferTests
{
    #region Public API
    /// <summary>All transforms retain column order and frame scalars occupy their declared std140 slots.</summary>
    [Fact]
    public void CapturePreservesMatricesAndExactUnsignedFrameIndex()
    {
        // Arrange: use distinct, non-symmetric matrices and a frame index beyond float integer precision.
        using var buffer = new VgeFrameUniformBuffer();
        float[][] matrices = CreateMatrices();
        const uint frameIndex = 16_777_217;
        // Act
        Capture(buffer, matrices, frameIndex);

        // Assert: packed slots preserve matrix order, scalar values, and the exact uint frame index.
        int[] matrixOffsets = [VgeFrameUniformBuffer.ProjectionOffset, VgeFrameUniformBuffer.ViewOffset,
            VgeFrameUniformBuffer.InverseProjectionOffset, VgeFrameUniformBuffer.InverseViewOffset,
            VgeFrameUniformBuffer.PreviousViewProjectionOffset, VgeFrameUniformBuffer.CurrentViewProjectionOffset];
        for (int matrix = 0; matrix < matrixOffsets.Length; matrix++)
            Assert.Equal(matrices[matrix], MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(matrixOffsets[matrix], VgeFrameUniformBuffer.MatrixSizeBytes)).ToArray());
        Assert.Equal(new Vector2(1920, 1080), buffer.ScreenSize);
        Assert.Equal(12.5f, UboPacking.ReadFloat(buffer.Bytes, VgeFrameUniformBuffer.TimeSecondsOffset));
        Assert.Equal(frameIndex, buffer.FrameIndex);
        Assert.Equal(new float[] { 11, 22, 33, .7f }, MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(VgeFrameUniformBuffer.CameraPositionOffset, 16)).ToArray());
        Assert.Equal(new Vector4(.1f, .2f, .3f, .4f), buffer.FogParameters);
        Assert.Equal(new VectorInt3(524288, -3, -524289), buffer.RenderOriginChunk);
        Assert.Equal(new Vector3(.25f, 17.5f, 31.75f), buffer.RenderOriginRemainder);
        Matrix4x4 expectedInverse = Matrix(matrices[2]) * Matrix(matrices[3]);
        float[] expected = [expectedInverse.M11, expectedInverse.M12, expectedInverse.M13, expectedInverse.M14,
            expectedInverse.M21, expectedInverse.M22, expectedInverse.M23, expectedInverse.M24,
            expectedInverse.M31, expectedInverse.M32, expectedInverse.M33, expectedInverse.M34,
            expectedInverse.M41, expectedInverse.M42, expectedInverse.M43, expectedInverse.M44];
        Assert.Equal(expected, MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(VgeFrameUniformBuffer.InverseViewProjectionOffset, VgeFrameUniformBuffer.MatrixSizeBytes)).ToArray());
        Assert.Equal(new Vector2(.125f, 2048), buffer.ClipPlanes);
        Assert.Equal(.016f, UboPacking.ReadFloat(buffer.Bytes, VgeFrameUniformBuffer.DeltaTimeOffset));
    }

    /// <summary>Repeated capture preserves the content revision while a one-bit frame change creates a new revision.</summary>
    [Fact]
    public void UnchangedCapturePreservesPendingRevision()
    {
        // Arrange: publish a baseline snapshot and retain its bytes and revision.
        using var buffer = new VgeFrameUniformBuffer();
        float[][] matrices = CreateMatrices();
        // Act: repeat the same snapshot, then change only one frame-index bit.
        Capture(buffer, matrices, 16_777_216);
        byte[] original = buffer.Bytes.ToArray();
        ulong revision = ReadRevision(buffer);
        Capture(buffer, matrices, 16_777_216);
        // Assert: unchanged publication retains its revision, while the changed index advances it once.
        Assert.True(buffer.IsDirty);
        Assert.Equal(revision, ReadRevision(buffer));
        Assert.Equal(original, buffer.Bytes.ToArray());

        Capture(buffer, matrices, 16_777_217);
        Assert.Equal(revision + 1, ReadRevision(buffer));
        Assert.Equal(original[..VgeFrameUniformBuffer.FrameIndexOffset], buffer.Bytes[..VgeFrameUniformBuffer.FrameIndexOffset].ToArray());
        Assert.Equal(original[(VgeFrameUniformBuffer.CameraPositionOffset)..], buffer.Bytes[(VgeFrameUniformBuffer.CameraPositionOffset)..].ToArray());
    }

    /// <summary>A malformed matrix in any position cannot publish partial transforms or advance the revision.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void InvalidMatrixPreservesCompleteSnapshot(int matrixIndex)
    {
        // Arrange: start with a valid generation and prepare a changed generation with one malformed matrix.
        using var buffer = new VgeFrameUniformBuffer();
        float[][] matrices = CreateMatrices();
        Capture(buffer, matrices, 9);
        byte[] original = buffer.Bytes.ToArray();
        ulong revision = ReadRevision(buffer);

        // Every preceding valid matrix also changes, exposing premature writes before validation completes.
        // Act: attempt snapshots with a nonfinite or undersized matrix.
        float[][] changed = CreateMatrices(1000);
        changed[matrixIndex][15] = float.NaN;
        Assert.Throws<ArgumentException>(() => Capture(buffer, changed, 10));
        // Assert: each failed attempt leaves the prior bytes and revision intact.
        Assert.Equal(original, buffer.Bytes.ToArray());
        Assert.Equal(revision, ReadRevision(buffer));

        changed[matrixIndex] = new float[15];
        Assert.Throws<ArgumentException>(() => Capture(buffer, changed, 10));
        Assert.Equal(original, buffer.Bytes.ToArray());
        Assert.Equal(revision, ReadRevision(buffer));
    }
    /// <summary>Invalid shared elapsed time cannot alter an already captured camera generation.</summary>
    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidDurationPreservesCompleteSnapshot(float deltaTime)
    {
        // Arrange: retain a valid generation before supplying the invalid shared time value.
        using var buffer = new VgeFrameUniformBuffer();
        float[][] matrices = CreateMatrices();
        Capture(buffer, matrices, 9);
        byte[] original = buffer.Bytes.ToArray();
        ulong revision = ReadRevision(buffer);
        // Act: capture using an invalid elapsed time.
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capture(matrices[0], matrices[1], matrices[2],
            matrices[3], matrices[4], matrices[5], new(1920, 1080), 20, 10, Vector3.Zero, Vector3.Zero, 0, deltaTime: deltaTime));
        // Assert: the rejected time preserves the last complete snapshot.
        Assert.Equal(original, buffer.Bytes.ToArray());
        Assert.Equal(revision, ReadRevision(buffer));
    }
    #endregion

    #region Private
    /// <summary>Creates six distinguishable non-symmetric transforms so packing errors cannot hide behind identities.</summary>
    private static float[][] CreateMatrices(float addition = 0)
        => Enumerable.Range(0, 6).Select(matrix => Enumerable.Range(0, 16)
            .Select(element => addition + matrix * 100 + element + 1f).ToArray()).ToArray();

    /// <summary>Captures a consistent camera fixture with distinct frame, position and fog values.</summary>
    private static void Capture(VgeFrameUniformBuffer buffer, float[][] matrices, uint frameIndex)
        => buffer.Capture(matrices[0], matrices[1], matrices[2], matrices[3], matrices[4], matrices[5],
            new Vector2(1920, 1080), 12.5f, frameIndex, new Vector3(11, 22, 33),
            new Vector3(.1f, .2f, .3f), .4f, new Vector2(.125f, 2048), new VanillaGraphicsExpanded.Numerics.VectorInt3(524288, -3, -524289),
            new Vector3(.25f, 17.5f, 31.75f), deltaTime: .016f, fogMinimum: .7f);

    /// <summary>Interprets GLSL columns as equivalent Numerics rows for independent matrix composition.</summary>
    private static Matrix4x4 Matrix(float[] m) => new(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],
        m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);

    /// <summary>Observes the existing publication revision without introducing a production test-only API.</summary>
    private static ulong ReadRevision(CpuUniformBuffer buffer)
        => (ulong)typeof(CpuUniformBuffer).GetField("contentRevision", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(buffer)!;
    #endregion
}
