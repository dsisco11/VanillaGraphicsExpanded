using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies the shared frame block's binary interface and atomic CPU snapshot capture.</summary>
public sealed class VgeFrameUniformBufferTests
{
    #region Public API
    /// <summary>All transforms retain column order and frame scalars occupy their declared std140 slots.</summary>
    [Fact]
    public void CapturePreservesMatricesAndExactUnsignedFrameIndex()
    {
        using var buffer = new VgeFrameUniformBuffer();
        float[][] matrices = CreateMatrices();
        const uint frameIndex = 16_777_217;
        Capture(buffer, matrices, frameIndex);

        Assert.Equal(544, buffer.SizeBytes);
        for (int matrix = 0; matrix < 6; matrix++)
            Assert.Equal(matrices[matrix], MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(matrix * 64, 64)).ToArray());
        Assert.Equal(new float[] { 1920, 1080, 12.5f }, MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(384, 12)).ToArray());
        Assert.Equal(frameIndex, MemoryMarshal.Read<uint>(buffer.Bytes[396..]));
        Assert.Equal(new float[] { 11, 22, 33, .7f }, MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(400, 16)).ToArray());
        Assert.Equal(new float[] { .1f, .2f, .3f, .4f }, MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(416, 16)).ToArray());
        Assert.Equal(new int[] { 524288, -3, -524289, 0 }, MemoryMarshal.Cast<byte, int>(buffer.Bytes.Slice(512, 16)).ToArray());
        Assert.Equal(new float[] { .25f, 17.5f, 31.75f, 0 }, MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(528, 16)).ToArray());
        Matrix4x4 expectedInverse = Matrix(matrices[2]) * Matrix(matrices[3]);
        float[] expected = [expectedInverse.M11, expectedInverse.M12, expectedInverse.M13, expectedInverse.M14,
            expectedInverse.M21, expectedInverse.M22, expectedInverse.M23, expectedInverse.M24,
            expectedInverse.M31, expectedInverse.M32, expectedInverse.M33, expectedInverse.M34,
            expectedInverse.M41, expectedInverse.M42, expectedInverse.M43, expectedInverse.M44];
        Assert.Equal(expected, MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(432, 64)).ToArray());
        Assert.Equal(new float[] { .125f, 2048, .016f, 0 }, MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(496, 16)).ToArray());
    }

    /// <summary>Repeated capture preserves the content revision while a one-bit frame change creates a new revision.</summary>
    [Fact]
    public void UnchangedCapturePreservesPendingRevision()
    {
        using var buffer = new VgeFrameUniformBuffer();
        float[][] matrices = CreateMatrices();
        Capture(buffer, matrices, 16_777_216);
        byte[] original = buffer.Bytes.ToArray();
        ulong revision = ReadRevision(buffer);
        Capture(buffer, matrices, 16_777_216);
        Assert.True(buffer.IsDirty);
        Assert.Equal(revision, ReadRevision(buffer));
        Assert.Equal(original, buffer.Bytes.ToArray());

        Capture(buffer, matrices, 16_777_217);
        Assert.Equal(revision + 1, ReadRevision(buffer));
        Assert.Equal(original[..396], buffer.Bytes[..396].ToArray());
        Assert.Equal(original[400..], buffer.Bytes[400..].ToArray());
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
        using var buffer = new VgeFrameUniformBuffer();
        float[][] matrices = CreateMatrices();
        Capture(buffer, matrices, 9);
        byte[] original = buffer.Bytes.ToArray();
        ulong revision = ReadRevision(buffer);

        // Every preceding valid matrix also changes, exposing premature writes before validation completes.
        float[][] changed = CreateMatrices(1000);
        changed[matrixIndex][15] = float.NaN;
        Assert.Throws<ArgumentException>(() => Capture(buffer, changed, 10));
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
        using var buffer = new VgeFrameUniformBuffer();
        float[][] matrices = CreateMatrices();
        Capture(buffer, matrices, 9);
        byte[] original = buffer.Bytes.ToArray();
        ulong revision = ReadRevision(buffer);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capture(matrices[0], matrices[1], matrices[2],
            matrices[3], matrices[4], matrices[5], new(1920, 1080), 20, 10, Vector3.Zero, Vector3.Zero, 0, deltaTime: deltaTime));
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
