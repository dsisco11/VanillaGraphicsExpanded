using System.Runtime.InteropServices;
using System.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks the shared light ABI, full capacity and atomic snapshot ownership.</summary>
public sealed class VgeLightsUniformBufferTests
{
    #region Public API

    #region Native light certification

    /// <summary>Native effect certificates follow the exact accepted raw payload and are withdrawn for authored candela.</summary>
    [Fact]
    public void NativeEffectCertificateTracksAtomicPublication()
    {
        // Arrange: publish native arrays that support certification.
        using var lights = new VgeLightsUniformBuffer();
        // Act: attempt a malformed replacement, then publish authored candela sources.
        lights.Capture(2, new float[6], [200, 400, 100, 2, 9, 3]);
        var admitted = lights.NativeEffectBounds;
        // Assert: malformed input keeps the certified generation atomic, and authored input withdraws the certificate.
        Assert.True(admitted.Supported);
        Assert.InRange(admitted.CloudMaximum, 9, 9.0004f);
        Assert.InRange(admitted.CloudMapMaximum, 9, 9.0004f);
        byte[] bytes = lights.Bytes.ToArray();
        Assert.Throws<ArgumentException>(() => lights.Capture(1, new float[3], [float.NaN, 1, 1]));
        Assert.Equal(admitted, lights.NativeEffectBounds);
        Assert.Equal(bytes, lights.Bytes.ToArray());
        lights.CaptureAuthored([new(Vector3.Zero, Vector3.One, 1000, 10)]);
        Assert.False(lights.NativeEffectBounds.Supported);
    }

    /// <summary>Invalid native domains and finite intermediate overflow cannot certify a normalized final result.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void NativeEffectCertificateRejectsUnsafeExpressions(int kind)
    {
        // Arrange: each kind selects one invalid native light-domain or overflow scenario.
        float[] color = kind switch
        {
            0 => [-1, 1, 1], 1 => [float.NaN, 1, 1], 2 => [1, float.PositiveInfinity, 1],
            3 => [0, 1, 1], 4 => [1, 2e19f, 0], 5 => [1, 1e19f, 0], _ => [1, float.NegativeInfinity, 1]
        };
        int count = kind == 5 ? 100 : 1;
        float[] colors = Enumerable.Range(0, count).SelectMany(_ => color).ToArray();
        // Act: evaluate certification for the selected unsafe input and a valid dim source.
        Assert.False(SceneColorPointLightBounds.FromEngine(count, colors).Supported);
        // Assert: unsafe expressions are rejected while valid dim input remains certifiable.
        Assert.True(SceneColorPointLightBounds.FromEngine(1, [0, .001f, .001f]).Supported);
    }

    #endregion

    #region Snapshot packing and ownership


    /// <summary>The matching bound sums converted active RGB and ignores unused capacity.</summary>
    [Fact]
    public void MatchingBoundUsesOnlyActiveConvertedSources()
    {
        using var lights = new VgeLightsUniformBuffer();
        // Arrange: include inactive capacity values that must not contribute to the bound.
        // Act: publish, attempt a malformed replacement, and then publish an empty active set.
        lights.Capture(2, [0, 0, 0, 1, 2, 3], [4, -5, 6, 3, 9, 2, 10000, 10000, 10000]);
        float expected = 9 * VgeFrameRenderer.CurrentLightingCalibration.PointCandelaPerUnit;
        // Assert: only converted active entries affect the maximum and invalid replacement is atomic.
        Assert.InRange(lights.SummedCandelaMaximum, expected, expected * 1.00004f);
        float retained = lights.SummedCandelaMaximum;
        Assert.Throws<ArgumentException>(() => lights.Capture(1, [0, 0, 0], [float.NaN, 1, 1]));
        Assert.Equal(retained, lights.SummedCandelaMaximum);
        lights.Capture(0, [], []);
        Assert.Equal(0, lights.SummedCandelaMaximum);
    }

    /// <summary>Authored flux is packed as chromaticity-normalized candela with a distinct positive influence radius.</summary>
    [Fact]
    public void AuthoredSourcesPublishCandelaAndRejectPartialInvalidUpdates()
    {
        using var lights = new VgeLightsUniformBuffer();
        // Arrange: publish one authored source with distinct position, color, flux, and radius values.
        // Act: read packed fields, then attempt invalid multi-source replacements.
        lights.CaptureAuthored([new(new(1, 2, 3), new(1, .4f, .1f), 1000, 20)]);
        // Assert: position, normalized chromaticity, radius, and atomic failure retain their contracts.
        Vector4 position = MemoryMarshal.Read<Vector4>(lights.Bytes.Slice(16, 16));
        Vector4 intensity = MemoryMarshal.Read<Vector4>(lights.Bytes.Slice(1616, 16));
        Assert.Equal(new Vector4(1, 2, 3, 0), position);
        Assert.Equal(20, intensity.W);
        double y = .2126 * intensity.X + .7152 * intensity.Y + .0722 * intensity.Z;
        Assert.InRange(y, 1000 / (4 * Math.PI) - .0001, 1000 / (4 * Math.PI) + .0001);
        Assert.InRange(intensity.Y / intensity.X, .399999, .400001);
        Assert.InRange(lights.SummedCandelaMaximum, intensity.X, intensity.X * 1.00004f);
        float retainedBound = lights.SummedCandelaMaximum;
        byte[] prior = lights.Bytes.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => lights.CaptureAuthored([
            new(Vector3.Zero, Vector3.One, 1000, 10), new(Vector3.Zero, Vector3.One, float.NaN, 10)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => lights.CaptureAuthored([new(Vector3.Zero, Vector3.One, 1, 0)]));
        Assert.Equal(prior, lights.Bytes.ToArray());
        Assert.Equal(retainedBound, lights.SummedCandelaMaximum);
    }

    /// <summary>Every supported entry retains original view coordinates and colors with std140 padding.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(100)]
    public void CapturesFullEngineCapacity(int count)
    {
        // Arrange: generate distinguishable position and color triples for the selected count.
        using var lights = new VgeLightsUniformBuffer();
        float[] positions = Enumerable.Range(0, count * 3).Select(i => i * .25f - 40).ToArray();
        float[] colors = Enumerable.Range(0, count * 3).Select(i => i * .5f).ToArray();
        // Act: capture the active entries into the full-capacity std140 block.
        lights.Capture(count, positions, colors);
        // Assert: all active fields are packed at their declared stride with zero padding.
        Assert.Equal(3216, lights.Bytes.Length);
        Assert.Equal((uint)count, MemoryMarshal.Read<uint>(lights.Bytes[..4]));
        Assert.All(lights.Bytes.Slice(4, 12).ToArray(), value => Assert.Equal((byte)0, value));
        for (int light = 0; light < count; light++)
        {
            Assert.Equal(positions.AsSpan(light * 3, 3).ToArray(), MemoryMarshal.Cast<byte, float>(lights.Bytes.Slice(16 + light * 16, 12)).ToArray());
            Assert.Equal(colors.AsSpan(light * 3, 3).ToArray(), MemoryMarshal.Cast<byte, float>(lights.Bytes.Slice(1616 + light * 16, 12)).ToArray());
            Assert.Equal(0f, MemoryMarshal.Read<float>(lights.Bytes.Slice(28 + light * 16)));
            Assert.Equal(0f, MemoryMarshal.Read<float>(lights.Bytes.Slice(1628 + light * 16)));
        }
        byte[] retained = lights.Bytes.ToArray();
        Array.Fill(positions, 999f); Array.Fill(colors, 999f);
        Assert.Equal(retained, lights.Bytes.ToArray());
    }

    /// <summary>Malformed lists cannot replace a valid generation or silently truncate the engine capacity.</summary>
    [Fact]
    public void InvalidCaptureLeavesPreviousSnapshotUntouched()
    {
        // Arrange: publish a valid snapshot and retain its bytes.
        using var lights = new VgeLightsUniformBuffer();
        lights.Capture(1, [1, 2, 3], [4, 5, 6]);
        byte[] before = lights.Bytes.ToArray();
        // Act: submit malformed counts, lengths, and values.
        Assert.Throws<ArgumentOutOfRangeException>(() => lights.Capture(101, new float[303], new float[303]));
        // Assert: every rejected attempt preserves the original generation bytes.
        Assert.Throws<ArgumentOutOfRangeException>(() => lights.Capture(-1, [], []));
        Assert.Throws<ArgumentException>(() => lights.Capture(2, [1, 2, 3], new float[6]));
        Assert.Throws<ArgumentException>(() => lights.Capture(1, [1, float.NaN, 3], [4, 5, 6]));
        Assert.Throws<ArgumentException>(() => lights.Capture(1, [1, 2, 3], [4, 5, float.PositiveInfinity]));
        Assert.Equal(before, lights.Bytes.ToArray());
    }

    #endregion

    #endregion
}
