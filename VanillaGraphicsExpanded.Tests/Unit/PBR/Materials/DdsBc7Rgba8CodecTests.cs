using System;
using System.IO;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class DdsBc7Rgba8CodecTests
{
    [Fact]
    public void RoundTrip_PreservesDimensions_AndIsReasonablyClose()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VGE", "DdsTests");
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, "bc7.dds");

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

        DdsBc7Rgba8Codec.WriteBc7Dds(path, width, height, src);

        byte[] dst = DdsBc7Rgba8Codec.ReadRgba8FromDds(path, out int rw, out int rh);

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
}
