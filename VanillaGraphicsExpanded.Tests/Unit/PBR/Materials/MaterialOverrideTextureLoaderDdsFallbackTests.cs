using System;
using System.Buffers.Binary;
using System.IO;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks the loader's fallback to linear RGBA16 UNORM data when BC decoding is unavailable.</summary>
[Trait("Category", "Unit")]
public sealed class MaterialOverrideTextureLoaderDdsFallbackTests
{
    #region Public API
    /// <summary>Decodes a DX10 RGBA16 DDS after the preferred BC path cannot supply the image.</summary>
    [Fact]
    public void DecodeDdsBytes_FallsBackToRgba16UnormDx10_WhenBcDecodeFails()
    {
        // Arrange: encode a 16-bit DX10 payload with endpoint channel values.
        using var stream = new MemoryStream();

        const int width = 4;
        const int height = 4;

        var srcU16 = new ushort[width * height * 4];
        for (int i = 0; i < srcU16.Length; i++)
        {
            srcU16[i] = (ushort)((i * 4099) ^ (i << 3));
        }

        // Endpoints.
        srcU16[0] = 0;
        srcU16[1] = 65535;

        DdsRgba16UnormCodec.WriteRgba16Unorm(stream, width, height, srcU16);

        byte[] bytes = stream.ToArray();

        // Act: decode through the loader's format-selection path.
        bool decoded = MaterialOverrideTextureLoader.TryDecodeDdsBytes(bytes, out var img, out string? reason, width, height);

        // Assert: the fallback selects the 16-bit format and keeps its endpoint values intact.
        Assert.True(decoded, reason);
        Assert.False(img.IsEmpty);
        Assert.Equal(width, img.Width);
        Assert.Equal(height, img.Height);
        Assert.Equal(MaterialOverrideTextureLoader.PixelDataFormat.Rgba16Unorm, img.Format);
        Assert.Equal(width * height * 4 * 2, img.Bytes.Length);

        // 0 and 65535 should survive intact (16-bit values).
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(img.Bytes.AsSpan(0, 2)));
        Assert.Equal((ushort)65535, BinaryPrimitives.ReadUInt16LittleEndian(img.Bytes.AsSpan(2, 2)));
    }
    #endregion
}
