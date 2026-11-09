using System;
using System.IO;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks BC7 DDS encoding preserves dimensions and keeps lossy channels within the expected budget.</summary>
[Trait("Category", "Unit")]
public sealed class DdsBc7Rgba8CodecTests
{
    #region Public API
    /// <summary>A BC7 DDS round trip preserves dimensions and remains within the per-channel error budget.</summary>
    [Fact]
    public void RoundTrip_PreservesDimensions_AndIsReasonablyClose()
    {
        // Arrange: build a small RGBA gradient that varies across both axes.
        using var stream = new MemoryStream();

        const int width = 8;
        const int height = 8;

        byte[] src = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;

                src[i + 0] = (byte)(x * 255 / (width - 1));
                src[i + 1] = (byte)(y * 255 / (height - 1));
                src[i + 2] = (byte)((x + y) * 255 / (width + height - 2));
                src[i + 3] = 255;
            }
        }

        // Act: encode as BC7, then decode back to RGBA8.
        DdsBc7Rgba8Codec.WriteBc7Dds(stream, width, height, src);

        stream.Position = 0;
        byte[] dst = DdsBc7Rgba8Codec.ReadRgba8FromDds(stream, out int rw, out int rh);

        // Assert: lossy compression keeps the image shape and each channel within tolerance.
        Assert.Equal(width, rw);
        Assert.Equal(height, rh);
        Assert.Equal(src.Length, dst.Length);

        // BC7 is lossy; just ensure it's within a small per-channel error budget.
        const int MaxDelta = 32;
        for (int i = 0; i < src.Length; i++)
        {
            int delta = Math.Abs(dst[i] - src[i]);
            Assert.InRange(delta, 0, MaxDelta);
        }
    }
    #endregion
}
