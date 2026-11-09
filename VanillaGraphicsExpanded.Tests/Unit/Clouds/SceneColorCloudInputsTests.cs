using VanillaGraphicsExpanded.PBR.SceneColor;

namespace VanillaGraphicsExpanded.Tests.Unit.Clouds;

/// <summary>Checks metadata covering native normalized cloud tiles and retained/upcoming owner input endpoints.</summary>
public sealed class SceneColorCloudInputsTests
{
    #region Public API
    /// <summary>The source-proven minimum length covers a pending view-distance rebuild at the same geometry radius.</summary>
    [Fact]
    public void MinimumPendingTileLengthConservativelyBoundsRetainedLength()
    {
        // Arrange: compare a pending short rebuild against the retained owner geometry.
        var minimum = SceneColorCloudInputs.FromInputs(42, 64, 20, 1, 0, 3, 2, 0, 0);
        // Act: derive the owner's full envelope for the retained input.
        var retained = SceneColorCloudInputs.FromInputs(42, 64, 200, 1, 0, 3, 2, 0, 0);
        // Assert: the pending envelope conservatively contains the retained draw and tile length.
        Assert.Equal(retained.PositionRadius, minimum.PositionRadius);
        Assert.True(minimum.VertexMaximum >= retained.VertexMaximum);
        Assert.Equal(64 * 20, minimum.CloudsLength);
    }

    /// <summary>Normalized short offsets, mesh corners, maximum thickness and native perception warp fit the captured radius.</summary>
    [Theory]
    [InlineData(0, 5f, 0f)] [InlineData(2, 5f, 360f)] [InlineData(2, -5f, 0f)]
    public void NativeGeometryEndpointsFitOwnerEnvelope(int effect, float intensity, float warp)
    {
        // Arrange and act: construct the owner bounds from the authored native geometry endpoints.
        var actual = SceneColorCloudInputs.FromInputs(42, 65, 31, 1, 0, -3, 2, effect, intensity);
        // Assert: normalized-short offsets, mesh extents, and warp fit the captured envelope.
        Assert.Equal(42u, actual.FrameIndex); Assert.Null(actual.Owner);
        Assert.Equal(65 * 31, actual.CloudsLength); Assert.Equal(3, actual.GlobalBrightnessMagnitude);
        // Evaluate the concrete normalized-short endpoint and integer mesh extents independently.
        double x = 32767 + 32 + warp, y = 16 + 4840 + warp;
        double radius = Math.Sqrt(x * x + y * y + x * x);
        Assert.InRange(actual.PositionRadius, radius, radius * 1.00004);
        Assert.True(actual.VertexMaximum > 3);
    }

    /// <summary>Night contributes no daytime color while an upcoming stronger signed brightness replaces the retained endpoint.</summary>
    [Fact]
    public void NightAndCloudTickGainUseActualEndpoints()
    {
        // Arrange: choose matching geometry with night, retained-day, upcoming signed-gain, and moon endpoints.
        // Act: calculate the night envelope.
        var night = SceneColorCloudInputs.FromInputs(42, 64, 32, 0, 1, 2, -7, 0, 0);
        // Assert: night suppresses cloud daylight while retaining absolute brightness metadata.
        Assert.Equal(0, night.VertexMaximum); Assert.Equal(7, night.GlobalBrightnessMagnitude);
        // Act: calculate the retained daytime envelope and the stronger upcoming signed value.
        var retained = SceneColorCloudInputs.FromInputs(42, 64, 32, 1, 0, -3, 2, 0, 0);
        var next = SceneColorCloudInputs.FromInputs(42, 64, 32, 1, 0, -3, -7, 0, 0);
        // Assert: the upcoming signed gain replaces the retained endpoint by its expected magnitude ratio.
        Assert.True(next.VertexMaximum > retained.VertexMaximum);
        Assert.InRange(next.VertexMaximum / retained.VertexMaximum, 7f / 3 - .00001f, 7f / 3 + .00001f);
        // Act: calculate the same upcoming endpoint with the moon contribution active.
        var moon = SceneColorCloudInputs.FromInputs(42, 64, 32, 1, 1, -3, -7, 0, 0);
        // Assert: the moon contribution applies its native attenuation to the same endpoint.
        Assert.InRange(moon.VertexMaximum / next.VertexMaximum, .049999f, .050001f);
    }

    /// <summary>Invalid typed source fields and overflowing temporary vertex intensity are rejected even at zero daylight.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void UnsupportedOwnerInputsCannotPublish(int kind)
    {
        // Arrange: author one invalid source field for each selected validation path.
        int size = kind == 0 ? 0 : 64, length = kind == 1 ? -1 : 32;
        float day = kind == 2 ? float.NaN : 0, moon = kind == 3 ? float.PositiveInfinity : 1;
        float retained = kind == 4 ? float.NaN : 3, next = kind == 5 ? float.PositiveInfinity : 2;
        float intensity = kind == 6 ? float.NaN : kind == 7 ? float.MaxValue : 0;
        // Act and assert: unsupported inputs cannot produce a published owner envelope.
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCloudInputs.FromInputs(42, size, length, day, moon, retained, next, 2, intensity));
    }
    #endregion
}
