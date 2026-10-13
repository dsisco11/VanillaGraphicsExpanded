using System;
using System.IO;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Verifies the exact linear byte payload and standard DDS container contract.</summary>
public sealed class DdsRgba8UnormCodecTests
{
    /// <summary>The standard DDS pixel-format offsets describe an exact linear RGBA8 payload.</summary>
    [Fact]
    public void HeaderUsesStandardDx10PixelFormatAndExactPayloadLength()
    {
        using var stream = new MemoryStream();
        DdsRgba8UnormCodec.WriteRgba8Unorm(stream, 2, 1, new byte[] {0, 1, 2, 255, 254, 253, 252, 255});
        byte[] bytes = stream.ToArray();
        Assert.Equal(156, bytes.Length);
        Assert.Equal(32u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(76, 4)));
        Assert.Equal(4u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80, 4)));
        Assert.Equal(0x30315844u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(84, 4)));
        Assert.Equal(28u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(128, 4)));
        stream.Position = 0;
        DdsRgba8UnormCodec.ReadRgba8UnormHeader(stream, out int width, out int height);
        Assert.Equal(2, width);
        Assert.Equal(1, height);
        Assert.Equal(148, stream.Position);
    }

    /// <summary>Round trips arbitrary byte channels without loss or dimension changes.</summary>
    [Fact]
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
