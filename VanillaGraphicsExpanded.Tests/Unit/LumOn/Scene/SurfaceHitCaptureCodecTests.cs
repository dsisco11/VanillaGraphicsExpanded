using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.LumOn.Scene.HitLighting;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks the CPU codec at the shader header and captured-record boundary.</summary>
public sealed class SurfaceHitCaptureCodecTests
{
    #region Public API
    /// <summary>Reusing a header resets producer state and stale suppression entries while preserving current identities.</summary>
    [Theory]
    [InlineData(0, 16)]
    [InlineData(20, 12)]
    [InlineData(32, 0)]
    public void HeaderClearsPreviousGenerationAndBoundsAdmission(int count, uint admitted)
    {
        // Arrange
        var header = Enumerable.Repeat(uint.MaxValue, SurfaceHitCaptureCodec.HeaderBytes >> 2).ToArray();
        var entries = Enumerable.Range(0, count).Select(i => Entry((uint)i + 1)).ToArray();
        // Act
        SurfaceHitCaptureCodec.EncodeHeader(header, 3, 17, entries);
        // Assert
        Assert.Equal(new uint[] { 0, admitted, 3, 17, (uint)count, 0, 0, 0 }, header[..8]);
        for (int i = 0; i < count; i++)
            Assert.Equal(new uint[] { (uint)i + 1, 2, 3, 4 }, header[(8 + (i << 2))..(12 + (i << 2))]);
        Assert.All(header[(8 + (count << 2))..], word => Assert.Equal(0u, word));
    }

    /// <summary>Only complete bounded records survive decoding, with exact descriptor values and estimator ownership.</summary>
    [Fact]
    public void DecodeRejectsIncompleteRecordsAndPreservesCompleteSamples()
 
    {
        // Arrange
        var records = new SurfaceHitCapture[5];
        records[0].QueryCount = 1;
        records[1].Complete = 1;
        records[2].Complete = 1; records[2].QueryCount = 65;
        records[3].Complete = 2; records[3].QueryCount = 1;
        records[4].Complete = 1; records[4].QueryCount = 2;
        records[4].Request = Entry(7).Request;
        records[4].Queries[0] = new SurfaceLightingQuery { Result = new(1, 2, 3, 4) };
        records[4].Queries[1] = new SurfaceLightingQuery { Result = new(5, 6, 7, 8) };
        // Act and Assert
        var result = Assert.Single(SurfaceHitCaptureCodec.Decode(records));
        var texel = Assert.Single(result.Texels);
        Assert.Equal(7u, texel.Request.Page);
        Assert.Equal(2, result.Queries.Length);
        Assert.Equal(1, result.Queries[0].Result.X);
        Assert.Equal(5, result.Queries[1].Result.X);
        Assert.Empty(result.Dependencies);
    }

    #endregion
    #region Private
    /// <summary>Creates a retained hit entry with fixed surface identity and a default lighting query.</summary>
    private static SurfaceHitRetryEntry Entry(uint page) => new(
        new SurfaceFallbackRequest { Page = page, Slot = 2, Patch = 3, Linear = 4 },
        [new SurfaceLightingQuery()], [], new(page, 3, 1, 1), new(null!, 1, 1, 1), 0);
    #endregion
}











