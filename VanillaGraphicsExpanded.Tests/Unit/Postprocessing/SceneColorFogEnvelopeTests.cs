using VanillaGraphicsExpanded.PBR.SceneColor;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Checks matching native authored fog endpoint admission without conflating color bounds with geometry.</summary>
public sealed class SceneColorFogEnvelopeTests
{
    #region Public API
    /// <summary>Only active spheres contribute their actual RGB maximum, and repeated colors form a maximum rather than a sum.</summary>
    [Theory]
    [InlineData(0, 5f)] [InlineData(1, 7f)] [InlineData(2, 9f)] [InlineData(3, 9f)]
    public void ActiveEndpointsPublishMatchingPeak(int quantity, float maximum)
    {
        // Arrange and act: expose only the selected native fog spheres to the envelope builder.
        float[] spheres = [0, 0, 0, 2, .2f, 7, 3, 5, 0, 0, 0, 2, .2f, 2, 9, 1, 0, 0, 0, 2, .2f, 4, 1, 8];
        var actual = SceneColorFogEnvelope.FromInputs(42, new(1, 2, 3), new(2, 4, 1), new(3, 1, 5), quantity, spheres);
        // Assert: only active RGB maxima contribute to the authored and decoded bound.
        Assert.Equal(42u, actual.FrameIndex); Assert.Equal(maximum, actual.AuthoredMaximum);
        double decoded = 32 * Math.Pow((maximum + .055) / 1.055, 2.4);
        Assert.InRange(actual.MaximumNits, decoded, decoded * 1.00004);
    }

    /// <summary>Inactive payload is ignored, while finite geometric signs remain separate from color admission.</summary>
    [Fact]
    public void InactivePayloadAndFiniteGeometryDoNotInventLighting()
    {
        // Arrange and act: provide invalid inactive payload followed by finite active geometry.
        Assert.Equal(0, SceneColorFogEnvelope.FromInputs(1, new(0), new(0), new(0), 0, [float.NaN]).MaximumNits);
        var actual = SceneColorFogEnvelope.FromInputs(1, new(0), new(0), new(0), 1, [-1, -2, -3, -4, -5, 2, 1, 0]);
        // Assert: geometric signs do not become a lighting maximum.
        Assert.Equal(2, actual.AuthoredMaximum);
    }

    /// <summary>All active geometry/color fields and the exact native sphere capacity are checked before publication.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void UnsupportedNativeFogCannotPublish(int kind)
    {
        // Arrange: corrupt one active field or the exact source-array shape per row.
        float[] sphere = [0, 0, 0, 2, .2f, 7, 3, 5];
        int count = kind == 8 ? 4 : kind == 9 ? -1 : kind == 10 ? 2 : 1;
        System.Numerics.Vector3 standard = new(1, 2, 3);
        if (kind < 8) sphere[kind] = kind < 5 ? float.NaN : -1;
        if (kind == 11) standard.X = float.MaxValue;
        // Act and assert: unsupported active native input is rejected before publication.
        Assert.ThrowsAny<ArgumentException>(() => SceneColorFogEnvelope.FromInputs(42, standard, new(0), new(0), count, sphere));
    }
    #endregion
}
