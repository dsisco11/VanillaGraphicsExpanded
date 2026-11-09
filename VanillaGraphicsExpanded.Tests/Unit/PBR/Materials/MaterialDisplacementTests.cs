using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.PBR.Materials;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks local physical-displacement bounds and tessellation-setting sanitization.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class MaterialDisplacementTests
{
    #region Public API
    #region Authoring validation
    /// <summary>Valid amplitudes retain metre units while invalid values opt out with a diagnostic.</summary>
    [Theory]
    [InlineData(0f, 0f, false)]
    [InlineData(.02f, .02f, false)]
    [InlineData(.05f, .05f, false)]
    [InlineData(-.01f, 0f, true)]
    [InlineData(.051f, 0f, true)]
    [InlineData(float.NaN, 0f, true)]
    [InlineData(float.PositiveInfinity, 0f, true)]
    public void AmplitudesAreBoundedOptIn(float input, float expected, bool invalid)
    {
        // Arrange: collect diagnostics to distinguish valid values from rejected input.
        var diagnostics = new List<string>();

        // Act: resolve the authored amplitude.
        float resolved = MaterialDisplacement.ResolveAmplitude(input, diagnostics.Add);

        // Assert: only invalid input reports an issue.
        Assert.Equal(expected, resolved);
        Assert.Equal(invalid ? 1 : 0, diagnostics.Count);
    }

    /// <summary>Invalid subdivision settings become finite, ordered bounds.</summary>
    [Fact]
    public void SubdivisionSettingsSanitizeNonFiniteAndOutOfRangeValues()
    {
        // Arrange: combine non-finite and out-of-range authored values.
        var settings = new TerrainSubdivisionSettings
        { MaximumLevel = 99, TargetEdgePixels = float.NaN, FadeStartMetres = float.PositiveInfinity, FadeEndMetres = -1 };

        // Act: sanitize the invalid values.
        settings.Sanitize();

        // Assert: sanitization clamps values and restores an ordered fade interval.
        Assert.Equal(8, settings.MaximumLevel);
        Assert.Equal(8, settings.TargetEdgePixels);
        Assert.Equal(4, settings.FadeStartMetres);
        Assert.Equal(5, settings.FadeEndMetres);

        // Arrange: cover the lower level bound, upper pixel bound, and invalid fade end.
        settings.MaximumLevel = 0;
        settings.TargetEdgePixels = 999;
        settings.FadeStartMetres = 200;
        settings.FadeEndMetres = float.NaN;
        // Act: sanitize the second set of invalid values.
        settings.Sanitize();

        // Assert: both lower and upper limits are enforced.
        Assert.Equal(1, settings.MaximumLevel);
        Assert.Equal(256, settings.TargetEdgePixels);
        Assert.Equal(127, settings.FadeStartMetres);
        Assert.Equal(128, settings.FadeEndMetres);
    }
    #endregion
    #endregion
}

