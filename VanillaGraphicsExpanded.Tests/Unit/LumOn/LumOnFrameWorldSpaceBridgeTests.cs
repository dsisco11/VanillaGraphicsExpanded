using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Numerics;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Checks signed chunk boundaries and sub-block precision of the player-origin bridge.</summary>
public sealed class LumOnFrameWorldSpaceBridgeTests
{
    #region Origin Decomposition
    /// <summary>Negative origins use floor division and large origins retain their fractional remainder.</summary>
    [Theory]
    [InlineData(-0.25, -1, 31.75)]
    [InlineData(-32.375, -2, 31.625)]
    [InlineData(32.125, 1, 0.125)]
    [InlineData(16777216.25, 524288, 0.25)]
    [InlineData(-16777216.25, -524289, 31.75)]
    public void PlayerOrigin_PreservesSignedCellAndFraction(double origin, int chunk, double remainder)
    {
        var result = LumOnFrameWorldSpaceBridge.Compute(origin, origin, origin);
        Assert.Equal(new VectorInt3(chunk, chunk, chunk), result.ChunkOffset);
        Assert.Equal(remainder, result.BlockOffsetRemainder.X);
        Assert.Equal(remainder, result.BlockOffsetRemainder.Y);
        Assert.Equal(remainder, result.BlockOffsetRemainder.Z);
        Assert.Equal(origin, chunk * 32.0 + (float)result.BlockOffsetRemainder.X);
    }
    #endregion
}
