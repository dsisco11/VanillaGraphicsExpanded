using System;
using System.IO;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks the exact dimensions and channel values of the RGBA16 UNORM DDS codec.</summary>
public sealed class DdsRgba16UnormCodecTests
{
    #region Public API
    /// <summary>Round-trips odd-sized RGBA16 data with exact channel values and dimensions.</summary>
    [Fact]
    public void RoundTrip_PreservesDimensions_AndExactValues()
    {
        // Arrange: use odd dimensions and varied values, including both 16-bit endpoints.
        using var stream = new MemoryStream();

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

        // Act: encode and decode the linear channel payload.
        DdsRgba16UnormCodec.WriteRgba16Unorm(stream, width, height, src);

        stream.Position = 0;
        ushort[] dst = DdsRgba16UnormCodec.ReadRgba16Unorm(stream, out int rw, out int rh);

        // Assert: dimensions and every channel are preserved exactly.
        Assert.Equal(width, rw);
        Assert.Equal(height, rh);
        Assert.Equal(src.Length, dst.Length);

        Assert.Equal(src, dst);
    }
    #endregion
}
