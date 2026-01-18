using System;
using System.IO;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

public sealed class DdsRgba16UnormCodecTests
{
    [Fact]
    public void RoundTrip_PreservesDimensions_AndExactValues()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VGE", "DdsTests");
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, "rgba16unorm.dds");

        const int width = 7;
        const int height = 5;

        ushort[] src = new ushort[width * height * 4];
        for (int i = 0; i < src.Length; i++)
        {
            // Spread values, include endpoints.
            src[i] = (ushort)((i * 1103) ^ (i << 3));
        }

        src[0] = 0;
        src[1] = 65535;

        DdsRgba16UnormCodec.WriteRgba16Unorm(path, width, height, src);

        ushort[] dst = DdsRgba16UnormCodec.ReadRgba16Unorm(path, out int rw, out int rh);

        Assert.Equal(width, rw);
        Assert.Equal(height, rh);
        Assert.Equal(src.Length, dst.Length);

        Assert.Equal(src, dst);
    }
}
