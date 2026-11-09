using VanillaGraphicsExpanded.Noise;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks fixed Squirrel3 constant values.</summary>
public sealed class Squirrel3NoiseTests
{
    #region Public API

    /// <summary>Fixed Squirrel3 constants retain the numeric values asserted by this test.</summary>
    [Fact]
    public void Constants_MatchShaderInclude()
    {
        // Arrange: no fixture is needed to check the fixed numeric literals.

        // Act: read the production constants.
        // Assert: each remains equal to its established literal value.
        Assert.Equal(198_491_317u, Squirrel3Noise.PrimeU1);
        Assert.Equal(6_542_989u, Squirrel3Noise.PrimeU2);
        Assert.Equal(786_433u, Squirrel3Noise.PrimeU3);
        Assert.Equal(3_039_394_381u, Squirrel3Noise.BitNoise1);
        Assert.Equal(1_759_714_724u, Squirrel3Noise.BitNoise2);
        Assert.Equal(458_671_337u, Squirrel3Noise.BitNoise3);
        Assert.Equal(4_294_967_295.0f, Squirrel3Noise.FloatMax);

    }

    #endregion
}
