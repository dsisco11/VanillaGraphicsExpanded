using System.Runtime.InteropServices;
using System.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks the shared light ABI, full capacity and atomic snapshot ownership.</summary>
public sealed class VgeLightsUniformBufferTests
{
    #region Public API
    /// <summary>Authored flux is packed as chromaticity-normalized candela with a distinct positive influence radius.</summary>
    [Fact]
    public void AuthoredSourcesPublishCandelaAndRejectPartialInvalidUpdates()
    {
        using var lights = new VgeLightsUniformBuffer();
        lights.CaptureAuthored([new(new(1, 2, 3), new(1, .4f, .1f), 1000, 20)]);
        Vector4 position = MemoryMarshal.Read<Vector4>(lights.Bytes.Slice(16, 16));
        Vector4 intensity = MemoryMarshal.Read<Vector4>(lights.Bytes.Slice(1616, 16));
        Assert.Equal(new Vector4(1, 2, 3, 0), position);
        Assert.Equal(20, intensity.W);
        double y = .2126 * intensity.X + .7152 * intensity.Y + .0722 * intensity.Z;
        Assert.InRange(y, 1000 / (4 * Math.PI) - .0001, 1000 / (4 * Math.PI) + .0001);
        Assert.InRange(intensity.Y / intensity.X, .399999, .400001);
        byte[] prior = lights.Bytes.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => lights.CaptureAuthored([
            new(Vector3.Zero, Vector3.One, 1000, 10), new(Vector3.Zero, Vector3.One, float.NaN, 10)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => lights.CaptureAuthored([new(Vector3.Zero, Vector3.One, 1, 0)]));
        Assert.Equal(prior, lights.Bytes.ToArray());
    }

    /// <summary>Every supported entry retains original view coordinates and colors with std140 padding.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(100)]
    public void CapturesFullEngineCapacity(int count)
    {
        using var lights = new VgeLightsUniformBuffer();
        float[] positions = Enumerable.Range(0, count * 3).Select(i => i * .25f - 40).ToArray();
        float[] colors = Enumerable.Range(0, count * 3).Select(i => i * .5f).ToArray();
        lights.Capture(count, positions, colors);
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
        using var lights = new VgeLightsUniformBuffer();
        lights.Capture(1, [1, 2, 3], [4, 5, 6]);
        byte[] before = lights.Bytes.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => lights.Capture(101, new float[303], new float[303]));
        Assert.Throws<ArgumentOutOfRangeException>(() => lights.Capture(-1, [], []));
        Assert.Throws<ArgumentException>(() => lights.Capture(2, [1, 2, 3], new float[6]));
        Assert.Throws<ArgumentException>(() => lights.Capture(1, [1, float.NaN, 3], [4, 5, 6]));
        Assert.Throws<ArgumentException>(() => lights.Capture(1, [1, 2, 3], [4, 5, float.PositiveInfinity]));
        Assert.Equal(before, lights.Bytes.ToArray());
    }
    #endregion
}
