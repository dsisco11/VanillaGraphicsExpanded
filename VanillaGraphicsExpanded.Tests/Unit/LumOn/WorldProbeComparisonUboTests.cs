using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Verifies independent ownership of packed diagnostic and ordinary parameter fields.</summary>
public sealed class WorldProbeComparisonUboTests
{
    #region Public API
    /// <summary>Updating anchor thresholds preserves suppression and vice versa.</summary>
    [Fact]
    public void Suppression_PreservesAnchorThreshold()
    {
        // Arrange
        var parameters = new LumOnProbeParamsUbo { DepthDiscontinuityThreshold = 0.25f };
        // Act and Assert
        parameters.SuppressWorldProbeRadiance = true;
        Assert.Equal(0.25f, parameters.DepthDiscontinuityThreshold);
        parameters.DepthDiscontinuityThreshold = 0.75f;
        Assert.True(parameters.SuppressWorldProbeRadiance);
        Assert.True(parameters.SuppressWorldProbeRadiance);
        parameters.SuppressWorldProbeRadiance = false;
        Assert.Equal(0.75f, parameters.DepthDiscontinuityThreshold);
        Assert.False(parameters.SuppressWorldProbeRadiance);
    }

    /// <summary>Updating AO strengths preserves readiness and vice versa.</summary>
    [Fact]
    public void Readiness_PreservesAoStrengths()
    {
        // Arrange
        var parameters = new LumOnDebugParamsUbo { DiffuseAOStrength = 0.25f, SpecularAOStrength = 0.5f };
        // Act and Assert
        parameters.WorldProbeComparisonReady = true;
        Assert.Equal(0.25f, parameters.DiffuseAOStrength);
        Assert.Equal(0.5f, parameters.SpecularAOStrength);
        parameters.DiffuseAOStrength = 0.75f;
        parameters.SpecularAOStrength = 1f;
        Assert.True(parameters.WorldProbeComparisonReady);
        Assert.True(parameters.WorldProbeComparisonReady);
        parameters.WorldProbeComparisonReady = false;
        Assert.Equal(0.75f, parameters.DiffuseAOStrength);
        Assert.Equal(1f, parameters.SpecularAOStrength);
        Assert.False(parameters.WorldProbeComparisonReady);
    }
    #endregion
}
