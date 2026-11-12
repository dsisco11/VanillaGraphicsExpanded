using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Checks the conservative terrain envelope against independent maxima of the authored shared waves.</summary>
public sealed class NativeShadowTerrainEnvelopeTests
{
    #region Public API
    /// <summary>Static terrain retains displacement but ignores controls for waves that do not execute.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.375)]
    public void StaticTerrainReturnsOnlyAuthoredDisplacement(double displacement)
    {
        // Arrange: set every wave input while disabling the material's waving branch.
        var inputs = new DefaultShaderUniforms
        {
            WindSpeed = 4, WindWaveIntensity = 3, WaterWaveIntensity = 5,
            GlobalWorldWarp = 2, GlitchWaviness = 1,
            PerceptionEffectId = 2, PerceptionEffectIntensity = 2
        };

        // Act: capture a non-waving terrain envelope.
        double result = NativeShadowTerrainEnvelope.Capture(inputs, waving: false, displacement);

        // Assert: no wave term contributes when the material branch is disabled.
        Assert.Equal(displacement, result);
    }

    /// <summary>Bounds foliage, liquid, global and perception motion independently and in combination.</summary>
    [Fact]
    public void WavingEnvelopeContainsEveryAuthoredMaterialBranch()
    {
        const double displacement = 0.1;
        const double windSpeed = 2;
        const double windIntensity = 3;
        const double waterIntensity = 5;
        const double globalWarp = 1.5;
        const double glitch = 0.5;
        const double perception = 1.25;

        // Each contribution is independently bounded using |sin(x)| <= 1; signs and phase cannot increase these maxima.
        double foliage = .25 * windIntensity * (1 + windSpeed);
        double liquid = waterIntensity / 5 + windIntensity * .05;
        double global = globalWarp * .05 + (glitch - .1) * .1;
        double twiceAppliedPerception = perception * .2;
        var inputs = new DefaultShaderUniforms
        {
            WindSpeed = (float)windSpeed, WindWaveIntensity = (float)windIntensity,
            WaterWaveIntensity = (float)waterIntensity, GlobalWorldWarp = (float)globalWarp,
            GlitchWaviness = (float)glitch, PerceptionEffectId = 2,
            PerceptionEffectIntensity = (float)perception
        };

        // Act: capture the single axis-independent extent used to expand candidate bounds.
        double actual = NativeShadowTerrainEnvelope.Capture(inputs, waving: true, displacement);

        // Assert: it covers either local material branch followed by global motion and every perception contribution.
        double localMaximum = Math.Max(foliage, liquid);
        double required = localMaximum + global + twiceAppliedPerception + displacement;
        Assert.True(double.IsFinite(actual));
        Assert.True(actual >= required);
        Assert.True(actual > required);
    }

    /// <summary>Negative global controls add no motion, while invalid displacement and unrepresentable bounds fail closed.</summary>
    [Fact]
    public void InvalidInputsFailClosedAndNegativeGlobalControlsDoNotExpandBounds()
    {
        var negative = new DefaultShaderUniforms
        {
            WindSpeed = 1, WindWaveIntensity = 1, WaterWaveIntensity = 0,
            GlobalWorldWarp = -100, GlitchWaviness = -5
        };
        var overflowing = new DefaultShaderUniforms
        {
            WindSpeed = float.MaxValue, WindWaveIntensity = float.MaxValue
        };
        var largeButBounded = new DefaultShaderUniforms { GlitchWaviness = float.MaxValue };

        // Act: evaluate clipped controls, a finite but unrepresentable product, and a large representable glitch wave.
        double actual = NativeShadowTerrainEnvelope.Capture(negative, waving: true, 0);
        double large = NativeShadowTerrainEnvelope.Capture(largeButBounded, waving: true, 0);

        // Assert: caller errors and float-unrepresentable geometry never produce a selectable envelope.
        Assert.True(double.IsFinite(actual));
        Assert.True(double.IsFinite(large));
        Assert.True(large > actual);
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeShadowTerrainEnvelope.Capture(negative, true, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeShadowTerrainEnvelope.Capture(negative, true, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeShadowTerrainEnvelope.Capture(negative, true, double.PositiveInfinity));
        Assert.Throws<InvalidOperationException>(() => NativeShadowTerrainEnvelope.Capture(overflowing, true, 0));
    }
    #endregion
}