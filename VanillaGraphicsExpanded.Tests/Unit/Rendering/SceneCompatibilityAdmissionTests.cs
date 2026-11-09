using VanillaGraphicsExpanded.PBR.SceneColor;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks unsupported compatibility inputs cannot masquerade as certified storage envelopes.</summary>
public sealed class SceneCompatibilityAdmissionTests
{
    #region Public API
    #region Cloud and particle inputs
    /// <summary>Vertex length, unclamped edge and modulation must be finite even when a later brightness gain is zero.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void NativeCloudVertexRejectsUnsupportedIntermediates(int kind)
    {
        // Arrange: make one intermediate invalid, including a zero-daylight masking case.
        float day = kind == 5 ? 0 : 1;
        float mode = kind == 2 ? 1.01f : 1;
        float radius = kind == 3 ? 1e20f : kind == 5 ? 1 : 1000;
        float length = kind switch { 0 => 0, 1 => float.NaN, 4 or 5 => float.Epsilon, _ => 1000 };
        // Act and assert: native cloud vertices with unsupported intermediate domains are rejected.
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.CloudVertexColor(day, 1, 2, mode, radius, length));
    }

    /// <summary>Invalid sources and overflowing native particle intermediates remain unsupported.</summary>
    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void CubeParticleArithmeticRejectsUnsupportedDomains(int kind)
    {
        // Arrange: vary one ambient, brightness, night, or point-source domain at a time.
        System.Numerics.Vector3 ambient = kind == 2 ? new(float.MaxValue) : new(1, 3, 2);
        var points = SceneColorPointLightBounds.FromEngine(0, []);
        float minimumBrightness = kind == 3 ? float.MaxValue : 1;
        float night = kind == 4 ? 1.01f : 0;
        if (kind == 5) points = default;
        // Act and assert: invalid native particle arithmetic cannot be admitted.
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.CubeParticles(ambient, night, minimumBrightness, 1, 1, 1, 1, points, 0, 1));
    }

    /// <summary>Dark native ambient values admit a finite bound without requiring an artificial illumination floor.</summary>
    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(1e-30f, 0)] [InlineData(1e-30f, 1)]
    public void DarkCubeParticleInputsPublishFiniteBounds(float ambient, float night)
    {
        // Arrange and act: evaluate the native dark-input corner without adding an illumination floor.
        var points = SceneColorPointLightBounds.FromEngine(0, []);
        float bound = SceneColorCompatibilityBounds.CubeParticles(new(ambient), night, 0, 1, 1, 0, 0, points, 0, 1);
        // Assert: the result remains finite and nonnegative.
        Assert.True(float.IsFinite(bound) && bound >= 0);
    }
    #endregion

    #region Lighting domains
    /// <summary>An active red component too small to survive normalization cannot qualify legacy range division.</summary>
    [Fact]
    public void NativePointNormalizedRedMustRemainNormal()
    {
        // Arrange and act: evaluate three normalized red channels around float-normal underflow.
        // Assert: subnormal decode cases are rejected while a normal channel is supported.
        Assert.False(SceneColorPointLightBounds.FromEngine(1, [1e-40f, 1, 1]).Supported);
        Assert.False(SceneColorPointLightBounds.FromEngine(1, [1e-30f, 1e10f, 0]).Supported);
        Assert.True(SceneColorPointLightBounds.FromEngine(1, [1e-20f, 1, 1]).Supported);
    }

    /// <summary>The native underwater extrapolation requires a complete finite unit-domain certificate.</summary>
    [Theory]
    [InlineData(-.01f, 1)] [InlineData(0, 1.01f)] [InlineData(1, 0)]
    [InlineData(float.NaN, 1)] [InlineData(0, float.PositiveInfinity)]
    public void ExtrapolatingOrInvalidMurkIsRejected(float minimum, float maximum)
    {
        // Arrange, act, and assert: every native lighting owner rejects incomplete murk domains.
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.NightSky(3, 2, 7, 0, 4, minimum, maximum));
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.Moon(3, 1, 6, 3, 7, true, minimum, maximum));
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.Clouds(3, 2, 1, 4, 5, 2, 7, minimum, maximum));
    }

    /// <summary>Missing normalized domains, nonfinite sources and derived decode overflow are rejected.</summary>
    [Fact]
    public void UnsafeSourceDomainsAndDerivedOverflowAreRejected()
    {
        // Arrange, act, and assert: reject invalid normalized sources and overflowing derived bounds.
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.CloudMap(1.01f, 1, 2, 3, 4, 5, 2));
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.CloudMap(1, float.NaN, 2, 3, 4, 5, 2));
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.Moon(1, 1.01f, 1, 1, 1, true, 0, 1));
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.PhysicalSky(1, float.PositiveInfinity, 1));
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.NightSky(float.MaxValue, 2, 7, 0, 0, 0, 1));
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.Moon(float.MaxValue, 1, 6, 3, 7, true, 0, 1));
        Assert.ThrowsAny<ArgumentException>(() => SceneColorCompatibilityBounds.Clouds(3, float.MaxValue, 1, 4, 5, 2, 7, 0, 1));
    }
    #endregion
    #endregion
}

