using System;
using System.IO;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

public sealed class DdsRgba8UnormCodecTests
{
    [Fact(Skip = "Material params cache switched to BC7; uncompressed RGBA8 DDS codec is no longer used by the cache.")]
    public void RoundTrip_PreservesDimensions_AndExactValues()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VGE", "DdsTests");
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, "rgba8unorm.dds");

        const int width = 9;
        const int height = 3;

        byte[] src = new byte[width * height * 4];
        for (int i = 0; i < src.Length; i++)
        {
            src[i] = (byte)((i * 29) ^ (i << 1));
        }

        src[0] = 0;
        src[1] = 255;

        DdsRgba8UnormCodec.WriteRgba8Unorm(path, width, height, src);

        byte[] dst = DdsRgba8UnormCodec.ReadRgba8Unorm(path, out int rw, out int rh);

        Assert.Equal(width, rw);
        Assert.Equal(height, rh);
        Assert.Equal(src.Length, dst.Length);

        Assert.Equal(src, dst);
    }
}
